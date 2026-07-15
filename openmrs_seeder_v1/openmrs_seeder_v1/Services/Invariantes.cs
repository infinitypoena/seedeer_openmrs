using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

/// <summary>Veredicto de una ley sobre una corrida.</summary>
/// <param name="Codigo">L1…L8.</param>
/// <param name="Nombre">La ley, en una frase.</param>
/// <param name="Aplica">
/// <c>false</c> = la corrida no da para juzgarla (una ventana de una semana no puede decir nada sobre si
/// los crónicos vuelven a su control trimestral). No es un aprobado: es un "no procede".
/// </param>
/// <param name="Cumple">Solo tiene sentido si <see cref="Aplica"/>.</param>
/// <param name="Medido">Lo que dio la corrida, con su umbral al lado.</param>
/// <param name="Pista">Qué mirar si está roja.</param>
public readonly record struct Ley(
    string Codigo,
    string Nombre,
    bool Aplica,
    bool Cumple,
    string Medido,
    string Pista);

/// <summary>
/// <b>Las leyes de la simulación.</b> Propiedades que una corrida <b>tiene que</b> cumplir para que los
/// datos que deja en OpenMRS describan una clínica y no un montón de filas plausibles. Se evalúan al
/// terminar (etapa 4/5) y una sola ley rota cambia el exit code: la corrida no se declara buena.
///
/// <para><b>Por qué existen.</b> El modelo de crecimiento (difusión de Bass) se añadió, rompió el corazón
/// del simulador —la continuidad longitudinal del paciente— y la corrida terminó en "completado" sin una
/// sola advertencia. Se descubrió tres meses después, mirando a mano los CSV de una corrida de 3,5 años:
/// el 79 % de los pacientes había venido una única vez, el <b>65 % de los crónicos jamás volvió a un
/// control</b> y la mitad de la agenda (14.025 citas) se había tirado a la basura. Ningún test lo cazó
/// porque todos los tests eran de piezas, y las piezas estaban bien: lo que estaba roto era el sistema.
/// Estas leyes son el test del sistema, y se corren sobre datos reales.</para>
///
/// <para>Cada ley lleva el número que dio la corrida rota, para que se vea de qué se está protegiendo.
/// Doc completo con el porqué de cada umbral: <c>leyes_simulacion.md</c>.</para>
///
/// Clase estática pura (sin red ni reloj) → testeable de forma determinista.
/// </summary>
public static class Invariantes
{
    // ── Umbrales. No son gustos: son lo que separa una clínica de un generador de filas. ───────────

    /// <summary>L1 — fracción mínima de crónicos que tienen que haber vuelto al menos una vez.</summary>
    public const double MinCronicosQueVuelven = 0.60;
    /// <summary>L2 — no-show máximo tolerable de la agenda (una clínica real pierde el 15-25 %).</summary>
    public const double MaxCitasPerdidas = 0.25;
    /// <summary>L5 — fracción máxima de días topando contra el techo DURO de seguridad.</summary>
    public const double MaxDiasEnElTecho = 0.10;
    /// <summary>L6 — fracción máxima del área de captación que puede acabar registrada en la clínica.</summary>
    public const double MaxPenetracionMercado = 0.50;
    /// <summary>L7 — visitas por paciente mínimas en una ventana larga.</summary>
    public const double MinVisitasPorPaciente = 2.0;
    /// <summary>L8 — desviación tolerable de la meseta respecto al objetivo pedido.</summary>
    public const double ToleranciaObjetivo = 0.20;

    /// <summary>Días de ventana por debajo de los cuales una corrida no puede juzgar la continuidad longitudinal.</summary>
    private const int DiasParaJuzgarCronicos = 90;
    private const int DiasParaJuzgarVisitasPorPaciente = 365;

    /// <summary>
    /// Evalúa las leyes sobre lo que la corrida <b>realmente sembró</b> (RunStats) y sobre el pool de
    /// pacientes al cierre. Es la única fuente de verdad: no proyecta ni estima nada.
    /// </summary>
    public static IReadOnlyList<Ley> Evaluar(
        RunStats stats, IReadOnlyList<SimulatedPatient> pool, SimulationSettings sim)
    {
        var cr          = sim.Crecimiento;
        var ventanaDias = (sim.EndDate.Date - sim.StartDate.Date).Days;
        var leyes       = new List<Ley>();

        // ── L1 · El crónico vuelve a su control ───────────────────────────────────────────────────
        // Un hipertenso o un diabético que pasa por la clínica y no vuelve NUNCA no es un paciente
        // crónico: es una anécdota. Es la ley que más duele romper, porque toda la historia clínica
        // longitudinal (problem list, programas, controles, re-órdenes) cuelga de ella.
        var cronicos       = pool.Count(p => p.CronicasActivas.Count > 0);
        var cronicosVuelven = pool.Count(p => p.CronicasActivas.Count > 0 && p.Visitas >= 2);
        var fraccionCronicos = cronicos == 0 ? 0 : (double)cronicosVuelven / cronicos;
        leyes.Add(new Ley(
            "L1", "El crónico vuelve a su control",
            Aplica: cronicos > 0 && ventanaDias >= DiasParaJuzgarCronicos,
            Cumple: fraccionCronicos >= MinCronicosQueVuelven,
            Medido: $"{Pct(fraccionCronicos)} de {cronicos} crónicos volvió al menos una vez " +
                    $"(umbral: ≥ {Pct(MinCronicosQueVuelven)})",
            Pista:  "La demanda de retorno del panel no se está atendiendo. Mirar si algo limita los " +
                    "recurrentes del día (un cupo) en vez de dejar que los ponga RecurrentSelector."));

        // ── L2 · La agenda se honra ───────────────────────────────────────────────────────────────
        // Si se cita a alguien y no se le atiende, la cita es decorado. El no-show real de una consulta
        // externa ronda el 15-25 % (aquí lo fija Appointments.AsistenciaProb).
        var citasResueltas = stats.CitasCompletadas + stats.CitasPerdidas;
        leyes.Add(new Ley(
            "L2", "La agenda se honra",
            Aplica: citasResueltas > 0,
            Cumple: stats.FraccionCitasPerdidas <= MaxCitasPerdidas,
            Medido: $"{Pct(stats.FraccionCitasPerdidas)} de {citasResueltas} citas se perdieron " +
                    $"({stats.CitasCompletadas} cumplidas / {stats.CitasPerdidas} no-show; " +
                    $"umbral: ≤ {Pct(MaxCitasPerdidas)})",
            Pista:  "Se están agendando más controles de los que la clínica puede atender. Comparar la " +
                    "probabilidad de citar (ReferralProbabilities.FollowUp*) con el aforo del día."));

        // ── L3 · Ninguna cita se pierde por falta de aforo ────────────────────────────────────────
        // El tripwire. Una consulta llena recorta LAS ALTAS, nunca a quien tenía cita. Este contador es
        // 0 por construcción; si deja de serlo es que alguien reintrodujo un cupo de recurrentes.
        leyes.Add(new Ley(
            "L3", "Ninguna cita se pierde por falta de aforo",
            Aplica: true,
            Cumple: stats.RetornosDesplazadosPorAforo == 0,
            Medido: $"{stats.RetornosDesplazadosPorAforo} retornos desplazados por el aforo " +
                    $"(umbral: 0) · {stats.NuevosRechazadosPorAforo} altas no captadas por consulta llena " +
                    "(esto sí es sano: es el freno)",
            Pista:  "Alguien está recortando los retornos para que quepan en un cupo. El aforo solo puede " +
                    "morder a las altas (SeedOrchestrator: el reparto del día)."));

        // ── L4 · El panel madura ──────────────────────────────────────────────────────────────────
        // Una clínica con tres años de historia NO puede tener el mismo mix nuevos/recurrentes que el día
        // que abrió: su panel de pacientes ha crecido y le genera consulta. Si el mix sale plano, el
        // volumen no lo está gobernando el panel.
        var anios = stats.PorAnio();
        var (mixPrimero, mixUltimo) = anios.Count >= 2
            ? (Mix(anios[0]), Mix(anios[^1]))
            : (0.0, 0.0);
        leyes.Add(new Ley(
            "L4", "El panel madura (la fracción de recurrentes sube con los años)",
            Aplica: anios.Count >= 2,
            Cumple: mixUltimo > mixPrimero,
            Medido: anios.Count >= 2
                ? $"recurrentes: {Pct(mixPrimero)} en {anios[0].Anio} → {Pct(mixUltimo)} en {anios[^1].Anio}"
                : "—",
            Pista:  "El mix está congelado: el volumen del día se está derivando de las altas en vez de " +
                    "sumarles la demanda real del panel."));

        // ── L5 · La curva es una curva, no una pared ──────────────────────────────────────────────
        // PacientesPorDiaMax es una RED DE SEGURIDAD. Si la clínica vive pegada a ella, no la gobierna el
        // objetivo: la gobierna el techo, y la curva de crecimiento es una rampa contra un muro.
        var fraccionTecho = stats.DiasConAtencion == 0
            ? 0
            : (double)stats.DiasEnElTecho / stats.DiasConAtencion;
        leyes.Add(new Ley(
            "L5", "La curva es una curva, no una pared",
            Aplica: cr.Enabled && stats.DiasConAtencion > 0,
            Cumple: fraccionTecho <= MaxDiasEnElTecho,
            Medido: $"{Pct(fraccionTecho)} de los {stats.DiasConAtencion} días topó con el techo duro de " +
                    $"{cr.PacientesPorDiaMax}/día (umbral: ≤ {Pct(MaxDiasEnElTecho)})",
            Pista:  "El boca a boca está sobrecalibrado: λ pide mucho más de lo que la clínica puede " +
                    "atender. Revisar CalibrarImitacion y que el objetivo sea alcanzable."));

        // ── L6 · No se capta a más gente de la que vive en el área ────────────────────────────────
        var penetracion = cr.PoblacionCaptacion <= 0
            ? 0
            : (double)stats.PacientesUnicos / cr.PoblacionCaptacion;
        leyes.Add(new Ley(
            "L6", "No se capta a más gente de la que vive en el área",
            Aplica: cr.Enabled && cr.PoblacionCaptacion > 0,
            Cumple: penetracion <= MaxPenetracionMercado,
            Medido: $"{stats.PacientesUnicos} pacientes distintos = {Pct(penetracion)} de un área de " +
                    $"{cr.PoblacionCaptacion} (umbral: ≤ {Pct(MaxPenetracionMercado)})",
            Pista:  "O la clínica capta demasiado (λ desbocada) o el área es demasiado pequeña " +
                    "(Crecimiento.PoblacionCaptacion)."));

        // ── L7 · Un paciente no es un ticket ──────────────────────────────────────────────────────
        // Si la media de visitas por paciente ronda 1, el simulador no está haciendo historias clínicas:
        // está haciendo altas. No hay evolución, ni control, ni continuidad — nada que un ETL o un
        // estudio longitudinal pueda explotar.
        leyes.Add(new Ley(
            "L7", "Un paciente no es un ticket",
            Aplica: ventanaDias >= DiasParaJuzgarVisitasPorPaciente && stats.PacientesUnicos > 0,
            Cumple: stats.VisitasPorPaciente >= MinVisitasPorPaciente,
            Medido: $"{stats.VisitasPorPaciente:0.00} visitas por paciente · " +
                    $"{Pct(FraccionQueVolvio(stats))} volvió alguna vez " +
                    $"(umbral: ≥ {MinVisitasPorPaciente:0.0} visitas)",
            Pista:  "Los pacientes se atienden una vez y se abandonan. Misma causa que L1."));

        // ── L8 · La clínica llega a donde se le pidió ─────────────────────────────────────────────
        // ⚠️ Solo se juzga si la corrida da tiempo a que la clínica LLEGUE: la rampa dura lo que la memoria
        // del boca a boca (`VentanaActividadDias`, 365 d por defecto). Exigirle a una corrida de 3 meses
        // que esté ya en su meseta de 25/día es un falso positivo — está creciendo, que es lo que se le pide.
        var objetivo = cr.PacientesPorDiaObjetivo;
        var desvio   = objetivo <= 0 ? 0 : Math.Abs(stats.MediaDiariaMaxima - objetivo) / objetivo;
        leyes.Add(new Ley(
            "L8", "La clínica llega a donde se le pidió (y no más)",
            Aplica: cr.Enabled && objetivo > 0 && ventanaDias >= cr.VentanaActividadDias,
            Cumple: desvio <= ToleranciaObjetivo,
            Medido: $"meseta {stats.MediaDiariaMaxima:0.0}/día contra un objetivo de {objetivo}/día " +
                    $"(desvío {Pct(desvio)}; umbral: ≤ {Pct(ToleranciaObjetivo)})",
            Pista:  "La calibración del boca a boca no está aterrizando en el objetivo. Comparar la " +
                    "proyección de la etapa 2/5 con la curva real de crecimiento_diario.csv."));

        return leyes;
    }

    /// <summary>
    /// Las leyes que se pueden juzgar <b>antes de sembrar</b>, sobre la proyección determinista de la
    /// etapa 2/5. No sustituyen a <see cref="Evaluar"/> (la proyección es una estimación, no la corrida),
    /// pero avisan de una configuración condenada <b>antes</b> de pasarse horas escribiendo en OpenMRS.
    /// </summary>
    public static IReadOnlyList<Ley> EvaluarProyeccion(
        IReadOnlyList<ProyeccionDia> curva, SimulationSettings sim)
    {
        var cr     = sim.Crecimiento;
        var leyes  = new List<Ley>();
        var dias   = curva.Where(d => d.Total > 0).ToList();
        if (dias.Count == 0) return leyes;

        var enElTecho     = dias.Count(d => d.Total >= cr.PacientesPorDiaMax);
        var fraccionTecho = (double)enElTecho / dias.Count;
        leyes.Add(new Ley(
            "L5", "La curva es una curva, no una pared",
            Aplica: cr.Enabled,
            Cumple: fraccionTecho <= MaxDiasEnElTecho,
            Medido: $"{Pct(fraccionTecho)} de los días proyectados toparía con el techo duro de " +
                    $"{cr.PacientesPorDiaMax}/día (umbral: ≤ {Pct(MaxDiasEnElTecho)})",
            Pista:  "Baja PacientesPorDiaObjetivo o sube PacientesPorDiaMax: tal como está, quien gobierna " +
                    "la clínica es el techo de seguridad."));

        var captados    = curva[^1].CaptadosTotal;
        var penetracion = cr.PoblacionCaptacion <= 0 ? 0 : (double)captados / cr.PoblacionCaptacion;
        leyes.Add(new Ley(
            "L6", "No se capta a más gente de la que vive en el área",
            Aplica: cr.Enabled && cr.PoblacionCaptacion > 0,
            Cumple: penetracion <= MaxPenetracionMercado,
            Medido: $"{captados} pacientes distintos proyectados = {Pct(penetracion)} de un área de " +
                    $"{cr.PoblacionCaptacion} (umbral: ≤ {Pct(MaxPenetracionMercado)})",
            Pista:  "Sube Crecimiento.PoblacionCaptacion: la clínica va a registrar a media ciudad."));

        return leyes;
    }

    /// <summary>Leyes que la corrida rompió (las que no aplican no cuentan).</summary>
    public static IReadOnlyList<Ley> Rotas(IEnumerable<Ley> leyes) =>
        leyes.Where(l => l.Aplica && !l.Cumple).ToList();

    private static double Mix((int Anio, int Total, int Nuevos, int Recurrentes) a) =>
        a.Total == 0 ? 0 : (double)a.Recurrentes / a.Total;

    private static double FraccionQueVolvio(RunStats stats) =>
        stats.PacientesUnicos == 0 ? 0 : (double)stats.PacientesQueVolvieron / stats.PacientesUnicos;

    private static string Pct(double fraccion) => $"{100 * fraccion:0.0} %";
}
