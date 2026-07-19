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
    private readonly Dictionary<int, Semana> _porAnio = [];
    private readonly Dictionary<string, int> _diagnosticos = new(StringComparer.OrdinalIgnoreCase);
    private readonly int[] _calificaciones = new int[6];   // índices 1..5 (el 0 no se usa)

    private sealed class Semana { public int Nuevos; public int Recurrentes; }

    public int TotalVisitas { get; private set; }
    public int VisitasDeNuevos { get; private set; }
    public int VisitasDeRecurrentes => TotalVisitas - VisitasDeNuevos;

    /// <summary>Recurrentes activos y satisfechos al cierre (los que sostienen el boca a boca).</summary>
    public int RecurrentesSatisfechosFinal { get; set; }
    /// <summary>Clientela de la clínica al cierre: pacientes que siguen viniendo (el A(d) de Bass).</summary>
    public int PacientesActivosFinal { get; set; }

    // ── La curva: lo ATENDIDO de verdad, día a día ────────────────────────────────────────────────
    //
    // ⚠️ Antes la "media efectiva" se derivaba de λ (λ / fracción de nuevos), es decir, del modelo — no de
    // la realidad. Cuando el aforo recortaba el volumen, el informe seguía cantando la media que el modelo
    // *pretendía*: en la corrida de 3,5 años decía 45/día porque λ pedía 101, y nadie veía que la clínica
    // llevaba dos años y medio contra el techo. La media sale ahora de lo que se sembró.

    private readonly List<int> _atendidosPorDia = [];

    /// <summary>Ventana de la media móvil (días con atención): un mes de consulta.</summary>
    public const int VentanaMediaMovil = 30;

    /// <summary>Media diaria con la que ARRANCÓ la clínica (primer mes de atención).</summary>
    public double MediaDiariaInicial => Media(_atendidosPorDia.Take(VentanaMediaMovil));
    /// <summary>Media diaria con la que TERMINÓ (último mes de atención).</summary>
    public double MediaDiariaFinal => Media(_atendidosPorDia.TakeLast(VentanaMediaMovil));
    /// <summary>
    /// Media diaria MÁXIMA (mejor mes). Hace falta junto a la final: si el área de captación se llena, la
    /// captación decae y la media final queda por debajo del pico — reportar solo el último mes haría
    /// parecer que la clínica encogió cuando en realidad creció y luego se topó con su mercado.
    /// </summary>
    public double MediaDiariaMaxima =>
        _atendidosPorDia.Count == 0
            ? 0
            : Enumerable.Range(0, Math.Max(1, _atendidosPorDia.Count - VentanaMediaMovil + 1))
                .Max(i => Media(_atendidosPorDia.Skip(i).Take(VentanaMediaMovil)));

    /// <summary>Media móvil de los últimos <see cref="VentanaMediaMovil"/> días con atención (la curva del CSV).</summary>
    public double MediaMovil => Media(_atendidosPorDia.TakeLast(VentanaMediaMovil));

    private static double Media(IEnumerable<int> xs)
    {
        var lista = xs as IList<int> ?? xs.ToList();
        return lista.Count == 0 ? 0 : lista.Average();
    }

    // ── Lo que las leyes de la simulación necesitan medir (ver Services/Invariantes.cs) ────────────

    /// <summary>Citas de control que el paciente cumplió (acudió).</summary>
    public int CitasCompletadas { get; private set; }
    /// <summary>Citas de control que vencieron sin que el paciente acudiera (no-show).</summary>
    public int CitasPerdidas { get; private set; }

    /// <summary>
    /// <b>Tripwire de la ley L3.</b> Retornos (citas de hoy + espontáneos) que la clínica <b>NO atendió por
    /// falta de aforo</b>. Tiene que ser <b>siempre 0</b>: una consulta llena recorta las altas, no le da
    /// plantón a quien tenía cita. Si alguien reintroduce un cupo de recurrentes, este contador se dispara
    /// y la ley L3 se pone roja — es exactamente el bug que costó 14.025 citas perdidas.
    /// </summary>
    public int RetornosDesplazadosPorAforo { get; private set; }

    /// <summary>Altas que no se pudieron captar porque la consulta estaba llena (sano: es el freno del aforo).</summary>
    public int NuevosRechazadosPorAforo { get; private set; }

    /// <summary>Remisiones al hospital emitidas en la corrida (obs "Remisiones solicitadas").</summary>
    public int RemisionesTotales { get; private set; }

    /// <summary>
    /// <b>Tripwire de la ley L9.</b> Remisiones emitidas en una visita que era el CONTROL post-alta de ese
    /// mismo episodio de referencia. Tiene que ser <b>siempre 0</b>: el control comprueba cómo salió del
    /// hospital, no lo vuelve a mandar. Si deja de serlo, el episodio de referencia se está encadenando —
    /// es el bug que dejó a un paciente con 47 encuentros de "Apendicitis aguda" y 6,8 remisiones por
    /// referido (corrida jul-2026).
    /// </summary>
    public int RemisionesEnControl { get; private set; }

    /// <summary>Días en los que la clínica topó con el techo DURO de seguridad (<c>PacientesPorDiaMax</c>).</summary>
    public int DiasEnElTecho { get; private set; }
    /// <summary>Días con atención simulados (denominador de <see cref="DiasEnElTecho"/>).</summary>
    public int DiasConAtencion => _atendidosPorDia.Count;

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
        _porAnio.Clear();
        _diagnosticos.Clear();
        Array.Clear(_calificaciones);
        _atendidosPorDia.Clear();
        TotalVisitas = 0;
        VisitasDeNuevos = 0;
        RecurrentesSatisfechosFinal = 0;
        PacientesActivosFinal = 0;
        CitasCompletadas = 0;
        CitasPerdidas = 0;
        RetornosDesplazadosPorAforo = 0;
        NuevosRechazadosPorAforo = 0;
        DiasEnElTecho = 0;
        RemisionesTotales = 0;
        RemisionesEnControl = 0;
    }

    /// <summary>Se emitió una remisión al hospital; <paramref name="enControl"/> = la visita era el control post-alta.</summary>
    public void RegistrarRemision(bool enControl)
    {
        RemisionesTotales++;
        if (enControl) RemisionesEnControl++;
    }

    /// <summary>Una cita de control se resolvió: el paciente acudió (<c>Completed</c>) o no (<c>Missed</c>).</summary>
    public void RegistrarCitaResuelta(bool cumplida)
    {
        if (cumplida) CitasCompletadas++;
        else CitasPerdidas++;
    }

    /// <summary>Cierra un día simulado: lo atendido y cómo se portó el aforo.</summary>
    public void RegistrarDiaSimulado(int atendidos, int nuevosRechazados, int retornosDesplazados, bool topoElTecho)
    {
        _atendidosPorDia.Add(atendidos);
        NuevosRechazadosPorAforo    += nuevosRechazados;
        RetornosDesplazadosPorAforo += retornosDesplazados;
        if (topoElTecho) DiasEnElTecho++;
    }

    /// <summary>
    /// Citas resueltas (cumplidas + perdidas) y qué fracción se perdió. Es la ley L2: una clínica pierde
    /// el 15-25 % de sus citas por no-show; si pierde la mitad, algo se las está tirando a la basura.
    /// </summary>
    public double FraccionCitasPerdidas =>
        CitasCompletadas + CitasPerdidas == 0
            ? 0
            : (double)CitasPerdidas / (CitasCompletadas + CitasPerdidas);

    /// <summary>Anota la nota (1-5) que un paciente le puso a una visita.</summary>
    public void RegistrarCalificacion(int nota)
    {
        if (nota is < 1 or > 5) return;
        _calificaciones[nota]++;
    }

    /// <summary>Cuántas visitas recibieron cada nota, del 1 al 5.</summary>
    public IReadOnlyList<(int Nota, int Veces)> Histograma() =>
        Enumerable.Range(1, 5).Select(n => (n, _calificaciones[n])).ToList();

    /// <summary>Total de visitas calificadas.</summary>
    public int VisitasCalificadas => _calificaciones.Sum();

    /// <summary>Nota media de todas las visitas de la corrida; 0 si no se calificó ninguna.</summary>
    public double CalificacionMedia =>
        VisitasCalificadas == 0
            ? 0
            : Enumerable.Range(1, 5).Sum(n => (double)n * _calificaciones[n]) / VisitasCalificadas;

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

        if (!_porAnio.TryGetValue(fecha.Year, out var a))
            _porAnio[fecha.Year] = a = new Semana();
        if (patient.EsNuevo) a.Nuevos++; else a.Recurrentes++;

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
    /// Visitas por año natural. Es la ley L4: <b>el panel madura</b> — la fracción de visitas recurrentes
    /// tiene que SUBIR con los años (una clínica con tres años de historia no puede tener el mismo mix que
    /// el día que abrió). Con el cupo fijo del 30 % salía plana: 31 % el mes 1 y 31 % el mes 42.
    /// </summary>
    public IReadOnlyList<(int Anio, int Total, int Nuevos, int Recurrentes)> PorAnio() =>
        _porAnio
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
