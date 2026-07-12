using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Estadísticas de lo que REALMENTE se sembró (no de lo planificado): el orquestador registra aquí cada
/// visita que se creó con éxito y <c>Program.cs</c> lo vuelca en el resumen final. Separado del
/// <see cref="SeedProgressTracker"/>, que solo lleva el avance y los errores de proceso.
/// Acumulador puro (sin red ni fechas del sistema) → testeable de forma determinista.
/// </summary>
public class RunStats
{
    private readonly Dictionary<string, int> _visitasPorPaciente = new(StringComparer.Ordinal);
    private readonly Dictionary<DateOnly, Semana> _porSemana = [];
    private readonly Dictionary<string, int> _diagnosticos = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Semana { public int Nuevos; public int Recurrentes; }

    public int TotalVisitas { get; private set; }
    public int VisitasDeNuevos { get; private set; }
    public int VisitasDeRecurrentes => TotalVisitas - VisitasDeNuevos;

    /// <summary>Pacientes distintos atendidos (un recurrente cuenta una sola vez).</summary>
    public int PacientesUnicos => _visitasPorPaciente.Count;

    /// <summary>Pacientes que volvieron al menos una vez (2+ visitas) — la continuidad longitudinal real.</summary>
    public int PacientesQueVolvieron => _visitasPorPaciente.Count(p => p.Value > 1);

    /// <summary>Visitas por paciente (media), indicador de cuánta historia clínica acumula cada uno.</summary>
    public double VisitasPorPaciente =>
        PacientesUnicos == 0 ? 0 : (double)TotalVisitas / PacientesUnicos;

    public void Reset()
    {
        _visitasPorPaciente.Clear();
        _porSemana.Clear();
        _diagnosticos.Clear();
        TotalVisitas = 0;
        VisitasDeNuevos = 0;
    }

    /// <summary>Registra una visita ya creada en OpenMRS (con todos sus diagnósticos, primario + comorbilidades).</summary>
    public void RegistrarVisita(SimulatedPatient patient, DateOnly fecha)
    {
        TotalVisitas++;
        if (patient.EsNuevo) VisitasDeNuevos++;

        if (!string.IsNullOrEmpty(patient.OpenMrsUuid))
            _visitasPorPaciente[patient.OpenMrsUuid] =
                _visitasPorPaciente.GetValueOrDefault(patient.OpenMrsUuid) + 1;

        var semana = InicioDeSemana(fecha);
        if (!_porSemana.TryGetValue(semana, out var s))
            _porSemana[semana] = s = new Semana();
        if (patient.EsNuevo) s.Nuevos++; else s.Recurrentes++;

        foreach (var dx in patient.TodosDiagnosticos)
        {
            var nombre = string.IsNullOrWhiteSpace(dx.NombreEs) ? dx.CielUuid : dx.NombreEs;
            _diagnosticos[nombre] = _diagnosticos.GetValueOrDefault(nombre) + 1;
        }
    }

    /// <summary>Lunes de la semana de esa fecha (agrupación semanal estable, sin depender de la cultura).</summary>
    public static DateOnly InicioDeSemana(DateOnly fecha) =>
        fecha.AddDays(-((int)fecha.DayOfWeek + 6) % 7);

    /// <summary>Visitas por semana (lunes que la abre), en orden cronológico.</summary>
    public IReadOnlyList<(DateOnly Semana, int Total, int Nuevos, int Recurrentes)> PorSemana() =>
        _porSemana
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value.Nuevos + kv.Value.Recurrentes, kv.Value.Nuevos, kv.Value.Recurrentes))
            .ToList();

    /// <summary>
    /// Diagnósticos más registrados (primarios + comorbilidades). El porcentaje es sobre el total de
    /// visitas: puede sumar más de 100 % entre todos, porque una visita puede llevar varios diagnósticos.
    /// </summary>
    public IReadOnlyList<(string Nombre, int Veces, double PctVisitas)> TopDiagnosticos(int n) =>
        _diagnosticos
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)   // desempate estable
            .Take(n)
            .Select(kv => (kv.Key, kv.Value, TotalVisitas == 0 ? 0 : 100.0 * kv.Value / TotalVisitas))
            .ToList();

    /// <summary>Diagnósticos distintos que llegaron a usarse (variedad real del catálogo en esta corrida).</summary>
    public int DiagnosticosDistintos => _diagnosticos.Count;
}
