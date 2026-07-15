using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

public class DailyScheduleGenerator
{
    private readonly SimulationSettings _settings;
    private readonly Random _rng;

    public DailyScheduleGenerator(SimulationSettings settings)
    {
        _settings = settings;
        _rng = new Random(settings.RandomSeed);
    }

    public IReadOnlyList<DailySchedule> Generate()
    {
        var result = new List<DailySchedule>();
        var current = DateOnly.FromDateTime(_settings.StartDate);
        var end = DateOnly.FromDateTime(_settings.EndDate);

        while (current <= end)
        {
            var weight = GetWeekdayWeight(current.DayOfWeek);
            int count;
            if (weight <= 0)
            {
                count = 0;
            }
            else
            {
                var mean = _settings.PacientesPorDiaMedio * weight;
                count = Math.Max(0, (int)Math.Round(SampleNormal(mean, mean * 0.20)));
            }

            // El plan solo dice cuántas ALTAS habrá (y solo se usa con el crecimiento apagado). Los
            // recurrentes no se pueden planificar: son la demanda del panel, y esa la mide el orquestador
            // sobre el pool real, día a día. Repartir el total con un PorcentajeRecurrentes fijo era el
            // origen del cupo que se comía la agenda — ver leyes_simulacion.md.
            result.Add(new DailySchedule
            {
                Date            = current,
                TotalPatients   = count,
                NuevosPacientes = count
            });

            current = current.AddDays(1);
        }

        return result.AsReadOnly();
    }

    private double GetWeekdayWeight(DayOfWeek day) => PesoDelDia(day, _settings.WeekdayWeights);

    /// <summary>
    /// Peso del día de la semana (0 = la clínica no abre). Público porque el modelo de crecimiento lo
    /// necesita para escalar la llegada de pacientes del día sin depender del plan precalculado.
    /// </summary>
    public static double PesoDelDia(DayOfWeek day, WeekdayWeightsSettings w) => day switch
    {
        DayOfWeek.Monday    => w.Monday,
        DayOfWeek.Tuesday   => w.Tuesday,
        DayOfWeek.Wednesday => w.Wednesday,
        DayOfWeek.Thursday  => w.Thursday,
        DayOfWeek.Friday    => w.Friday,
        DayOfWeek.Saturday  => w.Saturday,
        DayOfWeek.Sunday    => w.Sunday,
        _                   => 1.0
    };

    /// <summary>
    /// Peso medio de un día <b>abierto</b> (los cerrados, peso 0, no cuentan). Con él, "25 pacientes/día"
    /// significa 25 <b>de media en un día que la clínica abre</b>, y no 25 en el día tipo teórico: el aforo
    /// de cada día se escala por <c>peso / pesoMedio</c>, así el lunes cabe más y el sábado menos, pero la
    /// media a lo largo de la semana cae exactamente donde se pidió. 1.0 si no hay ningún día abierto.
    /// </summary>
    public static double PesoMedioDeApertura(WeekdayWeightsSettings w)
    {
        var pesos = Enum.GetValues<DayOfWeek>()
            .Select(d => PesoDelDia(d, w))
            .Where(p => p > 0)
            .ToList();
        return pesos.Count == 0 ? 1.0 : pesos.Average();
    }

    /// <summary>
    /// Genera una hora realista para una visita en el día dado,
    /// respetando los bloques pico configurados en HorarioAtencion.
    /// </summary>
    public DateTime GenerateVisitTime(DateOnly date)
    {
        var horario = _settings.HorarioAtencion;
        var pesoAM    = horario.PicoAM.Peso;
        var pesoPM    = horario.PicoPM.Peso;
        var pesoResto = Math.Max(0, 100 - pesoAM - pesoPM);

        var pick = _rng.NextDouble() * 100;
        TimeOnly time;

        if (pick < pesoAM)
        {
            var (hIni, mIni) = ParseTime(horario.PicoAM.Inicio);
            var (hFin, mFin) = ParseTime(horario.PicoAM.Fin);
            var totalMin = (hFin * 60 + mFin) - (hIni * 60 + mIni);
            var offset = _rng.Next(0, Math.Max(totalMin, 1));
            time = new TimeOnly(hIni, mIni).AddMinutes(offset);
        }
        else if (pick < pesoAM + pesoPM)
        {
            var (hIni, mIni) = ParseTime(horario.PicoPM.Inicio);
            var (hFin, mFin) = ParseTime(horario.PicoPM.Fin);
            var totalMin = (hFin * 60 + mFin) - (hIni * 60 + mIni);
            var offset = _rng.Next(0, Math.Max(totalMin, 1));
            time = new TimeOnly(hIni, mIni).AddMinutes(offset);
        }
        else
        {
            // Resto: distribuido entre 07:00 y 18:00
            var offset = _rng.Next(0, 660);
            time = new TimeOnly(7, 0).AddMinutes(offset);
        }

        return date.ToDateTime(time);
    }

    private static (int h, int m) ParseTime(string hhmm)
    {
        var parts = hhmm.Split(':');
        return (int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 0);
    }

    // Box-Muller transform para distribución normal
    private double SampleNormal(double mean, double stdDev)
    {
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = 1.0 - _rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * z;
    }
}
