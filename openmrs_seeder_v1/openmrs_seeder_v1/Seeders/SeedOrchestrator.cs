using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class SeedOrchestrator
{
    private readonly SimulationSettings _settings;
    private readonly DailyScheduleGenerator _schedule;
    private readonly PatientProfileGenerator _profiler;
    private readonly EpidemiologySelector _epiSelector;
    private readonly ClimateResolver _climate;
    private readonly PatientSeeder _patientSeeder;
    private readonly AllergySeeder _allergySeeder;
    private readonly VisitSeeder _visitSeeder;
    private readonly VitalsSeeder _vitalsSeeder;
    private readonly ConsultaSeeder _consultaSeeder;
    private readonly LabOrderSeeder _labOrderSeeder;
    private readonly LabWorkflowSeeder _labWorkflow;
    private readonly LabStaffAssigner _labStaff;
    private readonly PrescriptionSeeder _prescriptionSeeder;
    private readonly VisitCloseSeeder _visitCloseSeeder;
    private readonly ConditionSeeder _conditionSeeder;
    private readonly ProgramEnrollmentSeeder _programSeeder;
    private readonly AppointmentSeeder _appointmentSeeder;
    private readonly ClinicResourceAssigner _clinicResources;
    private readonly RunStats _stats;
    private readonly RunReportWriter _reportWriter;

    // La corrida es secuencial (un solo hilo, await tras await): el pool no necesita lock.
    private readonly List<SimulatedPatient> _patientPool = [];
    private readonly ILogger<SeedOrchestrator> _logger;

    /// <summary>
    /// El padrón de pacientes tal como quedó al terminar. Lo leen las <see cref="Invariantes">leyes de la
    /// simulación</see> (¿volvieron los crónicos? ¿cuántas visitas hizo cada uno?) y el CSV del padrón.
    /// </summary>
    public IReadOnlyList<SimulatedPatient> Pool => _patientPool;

    public SeedOrchestrator(
        SimulationSettings settings,
        DailyScheduleGenerator schedule,
        PatientProfileGenerator profiler,
        EpidemiologySelector epiSelector,
        ClimateResolver climate,
        PatientSeeder patientSeeder,
        AllergySeeder allergySeeder,
        VisitSeeder visitSeeder,
        VitalsSeeder vitalsSeeder,
        ConsultaSeeder consultaSeeder,
        LabOrderSeeder labOrderSeeder,
        LabWorkflowSeeder labWorkflow,
        LabStaffAssigner labStaff,
        PrescriptionSeeder prescriptionSeeder,
        VisitCloseSeeder visitCloseSeeder,
        ConditionSeeder conditionSeeder,
        ProgramEnrollmentSeeder programSeeder,
        AppointmentSeeder appointmentSeeder,
        ClinicResourceAssigner clinicResources,
        RunStats stats,
        RunReportWriter reportWriter,
        ILogger<SeedOrchestrator> logger)
    {
        _stats              = stats;
        _reportWriter       = reportWriter;
        _settings           = settings;
        _schedule           = schedule;
        _profiler           = profiler;
        _epiSelector        = epiSelector;
        _climate            = climate;
        _patientSeeder      = patientSeeder;
        _allergySeeder      = allergySeeder;
        _visitSeeder        = visitSeeder;
        _vitalsSeeder       = vitalsSeeder;
        _consultaSeeder     = consultaSeeder;
        _labOrderSeeder     = labOrderSeeder;
        _labWorkflow        = labWorkflow;
        _labStaff           = labStaff;
        _prescriptionSeeder = prescriptionSeeder;
        _visitCloseSeeder   = visitCloseSeeder;
        _conditionSeeder    = conditionSeeder;
        _programSeeder      = programSeeder;
        _appointmentSeeder  = appointmentSeeder;
        _clinicResources    = clinicResources;
        _logger             = logger;
    }

    /// <summary>
    /// Plan de días de la corrida (volumen diario por peso del día de la semana + normal). Se genera
    /// UNA sola vez y se cachea: <c>DailyScheduleGenerator</c> comparte su RNG con las horas de visita,
    /// así que regenerarlo desplazaría el flujo aleatorio y el informe previo no describiría la corrida
    /// que de verdad se ejecuta. Program.cs lo consume para el informe; <see cref="RunAsync"/> lo reusa.
    /// </summary>
    public IReadOnlyList<DailySchedule> PlanificarDias() => _plan ??= _schedule.Generate();
    private IReadOnlyList<DailySchedule>? _plan;

    public async Task RunAsync(Guid runId, SeedProgressTracker tracker, CancellationToken ct)
    {
        var days = PlanificarDias();
        var cr   = _settings.Crecimiento;

        // Seed fija: decide recurrentes/roster/espaciamiento — sin ella la reproducibilidad se rompe
        var rng         = new Random(_settings.RandomSeed + 10);
        // Flujos aleatorios propios (offsets libres): así activar el crecimiento o la satisfacción NO
        // desplaza el flujo de la recurrencia, y una corrida con la feature apagada sigue siendo
        // idéntica a las anteriores con la misma semilla.
        var rngSat      = new Random(_settings.RandomSeed + 19);
        var rngLlegadas = new Random(_settings.RandomSeed + 20);

        // Difusión de Bass. Los dos coeficientes se DERIVAN de los extremos de la curva, que son los dos
        // números que el usuario sí entiende: p del arranque (PacientesPorDiaMedio → el día 0, con el pool
        // vacío, la clínica atiende exactamente su volumen base de siempre) y q del destino
        // (PacientesPorDiaObjetivo → dónde se estabiliza). Ver BassGrowthModel.CalibrarImitacion.
        //
        // ⚠️ Bass gobierna LAS ALTAS y nada más. Los retornos los pone el panel (RecurrentSelector) y el
        // volumen del día es la SUMA de ambos. Antes el total se derivaba de las altas
        // (total = nuevos / (1 − PorcentajeRecurrentes)) y los recurrentes eran un residuo fijo del 30 %:
        // la mitad de las citas de control vencían sin que nadie las atendiera. Ver leyes_simulacion.md.
        var p = BassGrowthModel.CoeficienteInnovacion(_settings.PacientesPorDiaMedio, cr.PoblacionCaptacion);
        var q = BassGrowthModel.CoeficienteImitacion(days, _settings);

        // Con qué probabilidad vuelve por su cuenta, cada día, un paciente activo del pool que ya cumplió
        // su intervalo. Es la vía que hace que el padrón de pacientes SE USE (y no solo la agenda).
        var probRetornoEspontaneo = RecurrentSelector.ProbRetornoEspontaneoDiaria(_settings.Recurrence);

        bool Abierta(DailySchedule d) => cr.Enabled
            ? DailyScheduleGenerator.PesoDelDia(d.Date.DayOfWeek, _settings.WeekdayWeights) > 0
            : d.TotalPatients > 0;

        var diasConPacientes = days.Count(Abierta);

        // Asegurar consultorios + médicos y el personal de laboratorio (idempotente, fail-fast) antes
        // de repartir visitas: nada de encuentros firmados por un provider que no existe.
        await _clinicResources.InitializeAsync(ct);
        await _labStaff.InitializeAsync(ct);

        // Factor inicial: esta corrida se inclina a común con esta probabilidad (varía entre corridas)
        var runCommonP = _epiSelector.DrawRunCommonProbability();
        _epiSelector.ResetUsos(); // amortiguación anti-repetición: contadores limpios por corrida
        _stats.Reset();           // estadísticas de lo sembrado: limpias por corrida
        _reportWriter.Iniciar();  // CSV de la curva de crecimiento: se escribe día a día

        _logger.LogInformation(
            "[Orchestrator] Iniciando run {RunId} — {Dias} días con pacientes | P(común) de la corrida: {P:P0} | " +
            "crecimiento: {Crecimiento}",
            runId, diasConPacientes, runCommonP,
            cr.Enabled
                ? $"Bass — arranca en {_settings.PacientesPorDiaMedio}/día y apunta a {cr.PacientesPorDiaObjetivo}/día " +
                  $"(boca a boca derivado: 1 paciente nuevo por cada {(q > 0 ? 1 / q : 0):0.0} satisfechos)"
                : "desactivado (volumen fijo)");

        tracker.Update(runId, r =>
        {
            r.Etapa     = "simulando";
            r.TotalDias = days.Count;
        });

        int diasProcesados = 0;

        try
        {
            foreach (var day in days)
            {
                if (ct.IsCancellationRequested) break;
                if (!Abierta(day)) continue;

                // ── Volumen del día = ALTAS (Bass) + RETORNOS (el panel) ──────────
                // Una suma, no un cociente. Los retornos son la demanda REAL del pool (quién tiene cita hoy
                // y quién vuelve por su cuenta); las altas las pide Bass a partir del estado real del pool
                // (a cuánta gente del área atiende hoy la clínica y cuántos están contentos).
                var activos     = BassGrowthModel.PacientesActivos(_patientPool, day.Date, cr);
                var satisfechos = BassGrowthModel.RecurrentesActivosSatisfechos(_patientPool, day.Date, cr);
                var pesoDia     = DailyScheduleGenerator.PesoDelDia(day.Date.DayOfWeek, _settings.WeekdayWeights);
                var lambda      = BassGrowthModel.Lambda(p, q, cr.PoblacionCaptacion, activos, satisfechos);

                // 1) Los retornos, PRIMERO: son quienes tienen derecho preferente a la consulta. Se eligen
                //    sobre el pool tal como está hoy (aún sin las altas de hoy → no hay solapamiento
                //    posible: un paciente no puede ser nuevo y recurrente el mismo día).
                var elegibles = _patientPool
                    .Where(pac => pac.ProximoElegibleDesde is null || day.Date >= pac.ProximoElegibleDesde.Value)
                    .ToList();

                var seleccionados = RecurrentSelector.Seleccionar(
                    elegibles, day.Date,
                    _settings.Appointments.ToleranciaDias, _settings.Appointments.AsistenciaProb,
                    cr.AsistenciaProbInsatisfecho, probRetornoEspontaneo, cr.VentanaActividadDias, rng);

                // 2) Las altas, con lo que quede de aforo. La consulta llena deja de CAPTAR; jamás le da
                //    plantón a quien tenía cita (ley L3). Ese recorte es el freno más honesto del modelo.
                var demandaNuevos = cr.Enabled
                    ? BassGrowthModel.NuevosDelDia(lambda, pesoDia, rngLlegadas)
                    : day.NuevosPacientes;

                var aforo            = BassGrowthModel.CapacidadDelDia(_settings, pesoDia);
                var cupoNuevos       = Math.Max(0, Math.Min(demandaNuevos, aforo - seleccionados.Count));
                var nuevosRechazados = demandaNuevos - cupoNuevos;

                var totalDelDia = cupoNuevos + seleccionados.Count;
                var topoElTecho = totalDelDia >= cr.PacientesPorDiaMax;

                // Saturación de la consulta: cuánto se pasa hoy la clínica de lo que puede atender con
                // holgura. Es lo que degrada las calificaciones (y con ellas, el boca a boca).
                var saturacion = SatisfaccionPolicy.Saturacion(
                    totalDelDia, _settings.Satisfaccion.CapacidadComodaPorDia);

                _stats.RecurrentesSatisfechosFinal = satisfechos;
                _stats.PacientesActivosFinal       = activos;

                // Roster del día: 2-3 médicos "abren consultorio" (clínica pequeña, no siempre están todos)
                _clinicResources.ActivarMedicosDelDia(day.Date);

                // El laboratorio entrega hoy los resultados que tocaban (los que se procesan fuera tardan
                // días). No hace falta que el paciente vuelva: la muestra se procesa sin él.
                await _labWorkflow.ProcesarEntregasDelDiaAsync(_patientPool, day.Date, ct);

                var (estacion, tempC) = _climate.Resolve(day.Date);

                _logger.LogInformation(
                    "[Orchestrator] ── {Date} | {Nuevos} altas + {Recurrentes} retornos = {Total} visitas " +
                    "(aforo {Aforo}{Rechazo}) | S={S} satisfechos activos | clima: {Clima} ──",
                    day.Date.ToString("yyyy-MM-dd"), cupoNuevos, seleccionados.Count, totalDelDia, aforo,
                    nuevosRechazados > 0 ? $", {nuevosRechazados} altas sin captar" : "",
                    satisfechos, estacion ?? "—");

                tracker.Update(runId, r => r.FechaActual = day.Date.ToString("yyyy-MM-dd"));

                // Notas que los pacientes le ponen HOY a la clínica (para la fila del CSV del día).
                var notasDelDia = new List<int>();
                var nuevosDelDia = 0;

                // Alta de un paciente nuevo + su primera visita.
                async Task CrearYProcesarNuevoAsync()
                {
                    var patient = _profiler.GenerateNew(day.Date);
                    var uuid = await _patientSeeder.CreateAsync(patient, ct);
                    if (uuid is null)
                    {
                        _logger.LogWarning("[Orchestrator] Paciente {Id} no creado el {Date}, se omite",
                            patient.Identifier, day.Date.ToString("yyyy-MM-dd"));
                        tracker.Update(runId, r => r.Errores.Add(
                            $"[{day.Date}] No se pudo crear paciente {patient.Identifier}"));
                        return;
                    }
                    patient.OpenMrsUuid = uuid;

                    await _allergySeeder.SeedAsync(patient, ct);

                    patient.ClimaEstacion = estacion;
                    patient.TempAmbienteC = tempC;
                    var preferCommon = _epiSelector.RollPreferCommon(runCommonP);
                    patient.Categoria   = _epiSelector.SelectCategoria(patient.AgeGroup, patient.Gender, estacion, preferCommon);
                    patient.Diagnostico = _epiSelector.SelectDiagnostico(patient.Categoria, patient.AgeGroup, patient.Gender, estacion, preferCommon);
                    patient.Comorbilidades = patient.Diagnostico is null
                        ? []
                        : _epiSelector.SelectComorbilidades(patient.Diagnostico, patient.AgeGroup, patient.Gender, estacion);
                    patient.VisitDatetime = _schedule.GenerateVisitTime(day.Date);

                    await ProcesarVisitaAsync(patient, day.Date, tracker, runId, ct);

                    RegistrarCronicas(patient, patient);
                    RegistrarEpisodioAgudo(patient, patient, day.Date, fueControlAgudo: false);
                    FijarProximaVisita(patient, patient, day.Date, rng);
                    // Su primera impresión de la clínica (nadie llega con cita ni tiene médico "de siempre").
                    if (RegistrarCalificacion(patient, patient, acudioACita: false, totalDelDia, rngSat) is { } nota)
                        notasDelDia.Add(nota);
                    _patientPool.Add(patient);
                    nuevosDelDia++;
                    tracker.Update(runId, r => r.PacientesCreados++);
                }

                // ── Pacientes nuevos ──────────────────────────────────────────────
                for (int i = 0; i < cupoNuevos; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    await CrearYProcesarNuevoAsync();
                }

                // ── Pacientes recurrentes ─────────────────────────────────────────
                // Ya elegidos arriba (`seleccionados`): son la demanda real del panel, no un cupo.
                foreach (var base_ in seleccionados)
                {
                    if (ct.IsCancellationRequested) break;
                    var preferCommonRec = _epiSelector.RollPreferCommon(runCommonP);
                    // Edad recalculada a la fecha de la visita (usada también para elegir dx acorde a la edad).
                    var ageGroupVisita  = PatientProfileGenerator.GrupoEdad(base_.BirthDate, day.Date);

                    // Motivo de la visita: si viene a su cita, el dx que la motivó; si no, continuidad
                    // longitudinal probabilística (control de crónica / episodio agudo abierto).
                    var (dxSeguimiento, esControlCronico, esControlAgudo) = DxDeControl(
                        base_, day.Date,
                        _settings.Appointments.ToleranciaDias, _settings.VentanaSeguimientoAgudoDias,
                        () => _epiSelector.RollSeguimientoCronico(_settings.SeguimientoCronicoProb),
                        () => _epiSelector.RollSeguimientoAgudo(_settings.SeguimientoAgudoProb),
                        rng.Next);

                    var acudioACita = RecurrentSelector.TieneCitaHoy(
                        base_, day.Date, _settings.Appointments.ToleranciaDias);

                    // Si acude a su cita, lo atiende el médico con el que se agendó (no se re-sortea).
                    (string Location, string Provider)? citaRecursos =
                        acudioACita
                        && base_.ProximaCitaProviderUuid is { } prov && base_.ProximaCitaLocationUuid is { } loc
                            ? (loc, prov)
                            : null;

                    var recurrente = new SimulatedPatient
                    {
                        Identifier    = base_.Identifier,
                        OpenMrsUuid   = base_.OpenMrsUuid,
                        GivenName     = base_.GivenName,
                        SecondGivenName = base_.SecondGivenName,
                        FamilyName    = base_.FamilyName,
                        SecondFamilyName = base_.SecondFamilyName,
                        Gender        = base_.Gender,
                        BirthDate     = base_.BirthDate,
                        // Franja de edad recalculada a la fecha de ESTA visita (la edad avanza con el tiempo
                        // simulado), en vez de copiar la de la primera visita.
                        AgeGroup      = ageGroupVisita,
                        Address1      = base_.Address1,
                        City          = base_.City,
                        StateProvince = base_.StateProvince,
                        Country       = base_.Country,
                        EsNuevo       = false,
                        // Compartir historial de órdenes y lista de problemas con el paciente original
                        OrderedConcepts = base_.OrderedConcepts,
                        ProblemListConcepts = base_.ProblemListConcepts,
                        EnrolledPrograms = base_.EnrolledPrograms,
                        CitasPendientes = base_.CitasPendientes,
                        ResultadosPendientes = base_.ResultadosPendientes,
                        // Historial de calificaciones: compartido por referencia, como las colecciones de
                        // arriba — la nota de esta visita se acumula en el mismo paciente.
                        Calificaciones = base_.Calificaciones,
                        Insatisfecho   = base_.Insatisfecho,
                        UltimaVisita   = base_.UltimaVisita,
                        // Heredar el médico de cabecera (asignado en la primera visita del paciente)
                        CabeceraLocationUuid = base_.CabeceraLocationUuid,
                        CabeceraProviderUuid = base_.CabeceraProviderUuid,
                        // Antropometría basal: talla constante y peso derivando poco alrededor del IMC basal
                        TallaCm  = base_.TallaCm,
                        ImcBasal = base_.ImcBasal,
                        ClimaEstacion = estacion,
                        TempAmbienteC = tempC,
                        // Control de crónica → misma categoría; si no, se elige una nueva (motivo agudo).
                        Categoria     = dxSeguimiento?.Categoria
                                        ?? _epiSelector.SelectCategoria(ageGroupVisita, base_.Gender, estacion, preferCommonRec),
                        VisitDatetime = _schedule.GenerateVisitTime(day.Date)
                    };
                    recurrente.Diagnostico = dxSeguimiento
                        ?? _epiSelector.SelectDiagnostico(
                            recurrente.Categoria, ageGroupVisita, recurrente.Gender, estacion, preferCommonRec);
                    // En un control crónico, la lista de problemas es estable: se reutilizan las OTRAS crónicas
                    // ya conocidas del paciente en vez de sortear comorbilidades nuevas (que la hacían crecer
                    // sin fin visita a visita). En un motivo agudo, se sortean como antes.
                    recurrente.Comorbilidades = esControlCronico
                        ? base_.CronicasActivas.Where(d => d.CielUuid != dxSeguimiento!.CielUuid).ToList()
                        : recurrente.Diagnostico is null
                            ? []
                            : _epiSelector.SelectComorbilidades(recurrente.Diagnostico, ageGroupVisita, recurrente.Gender, estacion);

                    await ProcesarVisitaAsync(recurrente, day.Date, tracker, runId, ct, citaRecursos);

                    // Persistir en el paciente original cualquier crónica nueva surgida en esta visita.
                    RegistrarCronicas(base_, recurrente);
                    RegistrarEpisodioAgudo(base_, recurrente, day.Date, esControlAgudo);
                    FijarProximaVisita(base_, recurrente, day.Date, rng);
                    if (RegistrarCalificacion(base_, recurrente, acudioACita, totalDelDia, rngSat) is { } nota)
                        notasDelDia.Add(nota);
                }

                // ⚠️ Aquí ya NO se rellena ningún cupo con pacientes nuevos. No hay cupo: los retornos son
                // los que son (la demanda del panel) y las altas, las que quepan. Inventar altas para
                // cuadrar un total es justo lo que llenó la clínica de pacientes de una sola visita.

                _stats.RegistrarDiaSimulado(
                    atendidos:           nuevosDelDia + seleccionados.Count,
                    nuevosRechazados:    nuevosRechazados,
                    // Tripwire de la ley L3: el aforo NUNCA desplaza a un retorno. Si esto deja de ser 0,
                    // alguien ha vuelto a meter un cupo de recurrentes.
                    retornosDesplazados: 0,
                    topoElTecho:         topoElTecho);

                _reportWriter.RegistrarDia(new DiaDeCrecimiento(
                    Fecha:             day.Date,
                    Activos:           activos,
                    CaptadosTotal:     _patientPool.Count,
                    Satisfechos:       satisfechos,
                    Lambda:            lambda,
                    // La media que se grafica sale de lo ATENDIDO (media móvil de un mes), no de λ: antes se
                    // derivaba del modelo y cantaba 45/día mientras el aforo recortaba a la clínica.
                    MediaMovil:        _stats.MediaMovil,
                    Atendidos:         nuevosDelDia + seleccionados.Count,
                    Nuevos:            nuevosDelDia,
                    Recurrentes:       seleccionados.Count,
                    Aforo:             aforo,
                    NuevosRechazados:  nuevosRechazados,
                    CalificacionMedia: notasDelDia.Count == 0 ? 0 : notasDelDia.Average(),
                    Saturacion:        saturacion));

                diasProcesados++;
                var pct = (int)(diasProcesados * 100.0 / Math.Max(diasConPacientes, 1));
                tracker.Update(runId, r => { r.Porcentaje = pct; r.DiasProcesados = diasProcesados; });
            }

            if (!ct.IsCancellationRequested)
            {
                // Cierre del laboratorio: entregar los resultados cuya fecha cae dentro de la ventana pero
                // después del último día con atención (si no, quedarían en curso sin motivo).
                await _labWorkflow.ProcesarEntregasDelDiaAsync(
                    _patientPool, DateOnly.FromDateTime(_settings.EndDate), ct);

                // Cierre de agenda: las citas vencidas de pacientes que nunca volvieron pasan a Missed
                await _appointmentSeeder.SweepMissedAsync(_patientPool, DateOnly.FromDateTime(_settings.EndDate), ct);
            }
        }
        finally
        {
            // La evidencia de la corrida se escribe pase lo que pase: también si se cancela con Ctrl+C
            // (lo sembrado hasta ese momento persiste en OpenMRS y merece su CSV).
            _reportWriter.Finalizar(_patientPool, DateOnly.FromDateTime(_settings.EndDate));
        }

        var run = tracker.GetRun(runId);
        _logger.LogInformation(
            "[Orchestrator] Run {RunId} completado — {Pacientes} pacientes creados, {Errores} errores",
            runId, run?.PacientesCreados ?? 0, run?.Errores.Count ?? 0);

        tracker.Update(runId, r =>
        {
            r.Etapa      = "completado";
            r.Porcentaje = 100;
            r.Completado = true;
        });
    }

    /// <summary>
    /// Motivo de la visita de un paciente recurrente (seam puro, RNG/tiradas inyectadas):
    /// <list type="number">
    /// <item>Si <b>acude a su cita de control</b> (±tolerancia), el dx es el que la motivó — la consulta
    /// de seguimiento es del mismo cuadro por el que se le citó, sin tirar los dados.</item>
    /// <item>Si no, continuidad probabilística: control de una de sus crónicas (<paramref name="rollCronico"/>),</item>
    /// <item>o control del episodio agudo abierto si sigue vigente (<paramref name="rollAgudo"/>).</item>
    /// </list>
    /// <c>Dx = null</c> → el llamador sortea un motivo nuevo con el selector epidemiológico.
    /// </summary>
    public static (DiagnosticoEntry? Dx, bool EsControlCronico, bool EsControlAgudo) DxDeControl(
        SimulatedPatient poolPatient,
        DateOnly fecha,
        int toleranciaDias,
        int ventanaAgudoDias,
        Func<bool> rollCronico,
        Func<bool> rollAgudo,
        Func<int, int> nextInt)
    {
        if (RecurrentSelector.TieneCitaHoy(poolPatient, fecha, toleranciaDias) &&
            poolPatient.MotivoProximaCita is { } motivo)
            return (motivo, motivo.EsCronica, !motivo.EsCronica);

        if (poolPatient.CronicasActivas.Count > 0 && rollCronico())
            return (poolPatient.CronicasActivas[nextInt(poolPatient.CronicasActivas.Count)], true, false);

        if (poolPatient.UltimoDxAgudo is not null &&
            EpidemiologySelector.EpisodioAgudoVigente(
                poolPatient.FechaUltimoDxAgudo, fecha, ventanaAgudoDias) &&
            rollAgudo())
            return (poolPatient.UltimoDxAgudo, false, true);

        return (null, false, false);
    }

    /// <summary>
    /// Acumula en <paramref name="poolPatient"/> (el objeto persistente del pool) los diagnósticos
    /// crónicos surgidos en la visita de <paramref name="visitPatient"/>, deduplicados por UUID.
    /// Así el paciente "arrastra" sus crónicas y puede volver por ellas en visitas recurrentes.
    /// </summary>
    private static void RegistrarCronicas(SimulatedPatient poolPatient, SimulatedPatient visitPatient)
    {
        foreach (var dx in visitPatient.TodosDiagnosticos)
        {
            if (!dx.EsCronica) continue;
            if (poolPatient.CronicasActivas.Any(c => c.CielUuid == dx.CielUuid)) continue;
            poolPatient.CronicasActivas.Add(dx);
        }
    }

    /// <summary>
    /// Mantiene en el pool el episodio agudo abierto del paciente: una visita con dx primario agudo
    /// lo abre/renueva; su visita de CONTROL lo cierra (evita loops infinitos del mismo dx); un
    /// control crónico no lo toca (expira solo por ventana).
    /// </summary>
    private static void RegistrarEpisodioAgudo(
        SimulatedPatient poolPatient, SimulatedPatient visitPatient, DateOnly visita, bool fueControlAgudo)
    {
        if (fueControlAgudo)
        {
            poolPatient.UltimoDxAgudo = null;
            poolPatient.FechaUltimoDxAgudo = null;
            return;
        }

        var dx = visitPatient.Diagnostico;
        if (dx is null || dx.EsCronica) return;
        poolPatient.UltimoDxAgudo = dx;
        poolPatient.FechaUltimoDxAgudo = visita;
    }

    /// <summary>
    /// Fija en el paciente del pool cuándo puede volver. Si la consulta agendó un control
    /// (<see cref="SimulatedPatient.FechaSeguimiento"/>), la cita y la próxima elegibilidad son la MISMA
    /// fecha (la agenda gobierna el retorno), y la cita se lleva consigo el <b>motivo</b> (dx primario de
    /// esta visita) y el <b>médico</b> reservado: al acudir el paciente, esa consulta de seguimiento es
    /// del mismo cuadro y con el mismo médico. Si no, se impone solo el intervalo mínimo entre visitas
    /// (crónico = control mensual/trimestral; agudo = 1–3 semanas) y no queda cita que priorizar.
    /// </summary>
    /// <summary>
    /// El paciente califica la visita (1-5) y esa nota se acumula en su objeto del pool. No es un dato
    /// clínico: no se escribe en OpenMRS. Es lo que decide si seguirá viniendo y si recomendará la
    /// clínica (el boca a boca que hace crecer el volumen, ver <see cref="BassGrowthModel"/>).
    ///
    /// <para>La nota sale del contexto REAL de la visita: le atendió su médico de cabecera (continuidad
    /// asistencial), venía a una cita agendada, el cuadro era grave — y baja si ese día la consulta iba
    /// desbordada.</para>
    ///
    /// Devuelve <c>null</c> si la satisfacción está desactivada o si la visita no llegó a crearse en
    /// OpenMRS (no se puntúa una visita que no ocurrió).
    /// </summary>
    private int? RegistrarCalificacion(
        SimulatedPatient poolPatient, SimulatedPatient visitPatient,
        bool acudioACita, int pacientesDelDia, Random rngSat)
    {
        if (!_settings.Satisfaccion.Enabled) return null;
        if (string.IsNullOrEmpty(visitPatient.VisitUuid)) return null;

        var ctx = new ContextoVisita(
            AtendidoPorCabecera: !visitPatient.EsNuevo
                                 && !string.IsNullOrEmpty(visitPatient.AssignedProviderUuid)
                                 && visitPatient.AssignedProviderUuid == visitPatient.CabeceraProviderUuid,
            AcudioACita:     acudioACita,
            CuadroGrave:     visitPatient.TodosDiagnosticos.Any(
                                 d => d.Severidad.Equals("grave", StringComparison.OrdinalIgnoreCase)),
            PacientesDelDia: pacientesDelDia);

        var nota = SatisfaccionPolicy.Calificar(ctx, _settings.Satisfaccion, rngSat);
        poolPatient.Calificaciones.Add(nota);
        poolPatient.Insatisfecho = SatisfaccionPolicy.EsInsatisfecho(
            poolPatient.Calificaciones, _settings.Satisfaccion.UmbralSatisfaccion);
        _stats.RegistrarCalificacion(nota);
        return nota;
    }

    private void FijarProximaVisita(
        SimulatedPatient poolPatient, SimulatedPatient visitPatient, DateOnly visita, Random rng)
    {
        // Cuándo vino por última vez y cuántas veces ha venido: con esto se sabe si el paciente sigue
        // activo (y por tanto si todavía cuenta para el boca a boca y puede volver por su cuenta) o si ya
        // se alejó de la clínica. Solo cuenta lo que llegó a existir en OpenMRS: una visita que falló no
        // hace al paciente más "cliente" de lo que era.
        if (!string.IsNullOrEmpty(visitPatient.VisitUuid))
        {
            poolPatient.UltimaVisita = visita;
            poolPatient.Visitas++;
        }

        if (visitPatient.FechaSeguimiento is { } fs)
        {
            var cita = DateOnly.FromDateTime(fs);
            poolPatient.ProximaCita             = cita;
            poolPatient.ProximoElegibleDesde    = cita;
            poolPatient.MotivoProximaCita       = visitPatient.Diagnostico;
            poolPatient.ProximaCitaProviderUuid = visitPatient.ProximaCitaProviderUuid;
            poolPatient.ProximaCitaLocationUuid = visitPatient.ProximaCitaLocationUuid;
        }
        else
        {
            poolPatient.ProximaCita             = null;
            poolPatient.MotivoProximaCita       = null;
            poolPatient.ProximaCitaProviderUuid = null;
            poolPatient.ProximaCitaLocationUuid = null;
            poolPatient.ProximoElegibleDesde    = RecurrenceScheduler.ProximaFechaElegible(
                visita, poolPatient.CronicasActivas.Count > 0, rng, _settings.Recurrence);
        }
    }

    private async Task ProcesarVisitaAsync(
        SimulatedPatient patient,
        DateOnly date,
        SeedProgressTracker tracker,
        Guid runId,
        CancellationToken ct,
        (string Location, string Provider)? citaRecursos = null)
    {
        // Cancelación (Ctrl+C): no empezar una visita nueva — evita disparar POST que se cancelarán a
        // media máquina y ensuciarían el conteo de errores.
        if (ct.IsCancellationRequested) return;

        // Consultorio + médico de esta visita: el que le reservó la cita si viene a su control; si no,
        // los nuevos estrenan cabecera y los recurrentes vuelven a la suya con alta probabilidad.
        _clinicResources.AssignVisit(patient, citaRecursos);

        var visitUuid = await _visitSeeder.CreateAsync(patient, ct);
        if (visitUuid is null)
        {
            _logger.LogWarning("[Orchestrator] Visita no creada para {Id} el {Date}, se omite pipeline",
                patient.Identifier, date.ToString("yyyy-MM-dd"));
            tracker.Update(runId, r => r.Errores.Add(
                $"[{date}] No se pudo crear visita para {patient.Identifier}"));
            return;
        }
        patient.VisitUuid = visitUuid;
        // La visita existe en OpenMRS: cuenta para las estadísticas del resumen final.
        _stats.RegistrarVisita(patient, date);

        await _vitalsSeeder.SeedAsync(patient, ct);
        // El paciente "llegó": resolver sus citas pendientes (Completed si cae cerca, Missed si venció)
        await _appointmentSeeder.ResolverCitasAsync(patient, ct);
        await _consultaSeeder.SeedAsync(patient, ct);
        await _conditionSeeder.SeedAsync(patient, ct);
        await _programSeeder.SeedAsync(patient, ct);
        await _labOrderSeeder.SeedAsync(patient, ct);
        await _prescriptionSeeder.SeedAsync(patient, ct);
        // Reservar el médico del control (uno de los que estarán de turno ese día, preferentemente el
        // que lo atiende hoy) antes de agendar la cita real del seguimiento decidido en la consulta.
        if (patient.FechaSeguimiento is { } fechaSeguimiento)
            _clinicResources.ReservarCita(patient, DateOnly.FromDateTime(fechaSeguimiento));
        await _appointmentSeeder.SeedAsync(patient, ct);
        await _visitCloseSeeder.SeedAsync(patient, ct);
    }
}
