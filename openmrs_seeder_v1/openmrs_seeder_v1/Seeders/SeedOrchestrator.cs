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
    private readonly PrescriptionSeeder _prescriptionSeeder;
    private readonly VisitCloseSeeder _visitCloseSeeder;
    private readonly ConditionSeeder _conditionSeeder;
    private readonly ProgramEnrollmentSeeder _programSeeder;
    private readonly AppointmentSeeder _appointmentSeeder;
    private readonly ClinicResourceAssigner _clinicResources;

    // La corrida es secuencial (un solo hilo, await tras await): el pool no necesita lock.
    private readonly List<SimulatedPatient> _patientPool = [];
    private readonly ILogger<SeedOrchestrator> _logger;

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
        PrescriptionSeeder prescriptionSeeder,
        VisitCloseSeeder visitCloseSeeder,
        ConditionSeeder conditionSeeder,
        ProgramEnrollmentSeeder programSeeder,
        AppointmentSeeder appointmentSeeder,
        ClinicResourceAssigner clinicResources,
        ILogger<SeedOrchestrator> logger)
    {
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
        var days             = PlanificarDias();
        var diasConPacientes = days.Count(d => d.TotalPatients > 0);
        // Seed fija: decide recurrentes/roster/espaciamiento — sin ella la reproducibilidad se rompe
        var rng              = new Random(_settings.RandomSeed + 10);

        // Asegurar consultorios + médicos (idempotente) antes de repartir visitas
        await _clinicResources.InitializeAsync(ct);

        // Factor inicial: esta corrida se inclina a común con esta probabilidad (varía entre corridas)
        var runCommonP = _epiSelector.DrawRunCommonProbability();
        _epiSelector.ResetUsos(); // amortiguación anti-repetición: contadores limpios por corrida

        _logger.LogInformation("[Orchestrator] Iniciando run {RunId} — {Dias} días con pacientes | P(común) de la corrida: {P:P0}",
            runId, diasConPacientes, runCommonP);

        tracker.Update(runId, r =>
        {
            r.Etapa     = "simulando";
            r.TotalDias = days.Count;
        });

        int diasProcesados = 0;

        foreach (var day in days)
        {
            if (ct.IsCancellationRequested) break;
            if (day.TotalPatients == 0) continue;

            // Roster del día: 2-3 médicos "abren consultorio" (clínica pequeña, no siempre están todos)
            _clinicResources.ActivarMedicosDelDia(day.Date);

            var (estacion, tempC) = _climate.Resolve(day.Date);

            _logger.LogInformation("[Orchestrator] ── {Date} | {Nuevos} nuevos, {Recurrentes} recurrentes | clima: {Clima} ──",
                day.Date.ToString("yyyy-MM-dd"), day.NuevosPacientes, day.PacientesRecurrentes, estacion ?? "—");

            tracker.Update(runId, r => r.FechaActual = day.Date.ToString("yyyy-MM-dd"));

            // Alta de un paciente nuevo + su primera visita. Se reutiliza para rellenar el cupo de
            // recurrentes cuando el pool aún es pequeño (primeros días), en vez de perder ese volumen.
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
                _patientPool.Add(patient);
                tracker.Update(runId, r => r.PacientesCreados++);
            }

            // ── Pacientes nuevos ──────────────────────────────────────────────
            for (int i = 0; i < day.NuevosPacientes; i++)
            {
                if (ct.IsCancellationRequested) break;
                await CrearYProcesarNuevoAsync();
            }

            // ── Pacientes recurrentes ─────────────────────────────────────────
            // Excluir a los ya visitados hoy (nuevos del mismo día); elegibles = cumplieron su intervalo.
            var atendidosHoy = new HashSet<string>(
                _patientPool
                    .Where(p => p.VisitDatetime.Date == day.Date.ToDateTime(TimeOnly.MinValue).Date)
                    .Select(p => p.OpenMrsUuid));
            var elegibles = _patientPool
                .Where(p => !atendidosHoy.Contains(p.OpenMrsUuid))
                .Where(p => p.ProximoElegibleDesde is null || day.Date >= p.ProximoElegibleDesde.Value)
                .ToList();

            // La agenda gobierna el retorno: primero los pacientes con cita para hoy (±tolerancia), cada
            // uno con probabilidad de asistencia; el cupo restante se sortea entre el resto de elegibles.
            var seleccionados = RecurrentSelector.Seleccionar(
                elegibles, day.Date, day.PacientesRecurrentes,
                _settings.Appointments.ToleranciaDias, _settings.Appointments.AsistenciaProb, rng);

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

                // Si acude a su cita, lo atiende el médico con el que se agendó (no se re-sortea).
                (string Location, string Provider)? citaRecursos =
                    RecurrentSelector.TieneCitaHoy(base_, day.Date, _settings.Appointments.ToleranciaDias)
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
            }

            // El cupo de recurrentes no cubierto (pool aún pequeño, sobre todo los primeros días) se
            // rellena con pacientes nuevos para no perder ese volumen diario.
            for (int i = seleccionados.Count; i < day.PacientesRecurrentes; i++)
            {
                if (ct.IsCancellationRequested) break;
                await CrearYProcesarNuevoAsync();
            }

            diasProcesados++;
            var pct = (int)(diasProcesados * 100.0 / Math.Max(diasConPacientes, 1));
            tracker.Update(runId, r => { r.Porcentaje = pct; r.DiasProcesados = diasProcesados; });
        }

        // Cierre de agenda: las citas vencidas de pacientes que nunca volvieron pasan a Missed
        if (!ct.IsCancellationRequested)
        {
            await _appointmentSeeder.SweepMissedAsync(_patientPool, DateOnly.FromDateTime(_settings.EndDate), ct);
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
    private void FijarProximaVisita(
        SimulatedPatient poolPatient, SimulatedPatient visitPatient, DateOnly visita, Random rng)
    {
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

        await _vitalsSeeder.SeedAsync(patient, ct);
        // El paciente "llegó": resolver sus citas pendientes (Completed si cae cerca, Missed si venció)
        await _appointmentSeeder.ResolverCitasAsync(patient, ct);
        await _consultaSeeder.SeedAsync(patient, ct);
        await _conditionSeeder.SeedAsync(patient, ct);
        await _programSeeder.SeedAsync(patient, ct);
        // "Ya llegó el resultado": entregar los labs que quedaron pendientes de visitas anteriores
        await _labOrderSeeder.ProcesarPendientesAsync(patient, ct);
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
