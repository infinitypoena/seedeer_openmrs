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
    private readonly ClinicResourceAssigner _clinicResources;

    private readonly List<SimulatedPatient> _patientPool = [];
    private readonly Lock _poolLock = new();
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
        _clinicResources    = clinicResources;
        _logger             = logger;
    }

    public async Task RunAsync(Guid runId, SeedProgressTracker tracker, CancellationToken ct)
    {
        var days             = _schedule.Generate();
        var diasConPacientes = days.Count(d => d.TotalPatients > 0);
        var rng              = new Random();

        // Asegurar consultorios + médicos (idempotente) antes de repartir visitas
        await _clinicResources.InitializeAsync(ct);

        // Factor inicial: esta corrida se inclina a común con esta probabilidad (varía entre corridas)
        var runCommonP = _epiSelector.DrawRunCommonProbability();

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

            var (estacion, tempC) = _climate.Resolve(day.Date);

            _logger.LogInformation("[Orchestrator] ── {Date} | {Nuevos} nuevos, {Recurrentes} recurrentes | clima: {Clima} ──",
                day.Date.ToString("yyyy-MM-dd"), day.NuevosPacientes, day.PacientesRecurrentes, estacion ?? "—");

            tracker.Update(runId, r => r.FechaActual = day.Date.ToString("yyyy-MM-dd"));

            // ── Pacientes nuevos ──────────────────────────────────────────────
            for (int i = 0; i < day.NuevosPacientes; i++)
            {
                var patient = _profiler.GenerateNew(day.Date);
                var uuid = await _patientSeeder.CreateAsync(patient, ct);
                if (uuid is null)
                {
                    _logger.LogWarning("[Orchestrator] Paciente {Id} no creado el {Date}, se omite",
                        patient.Identifier, day.Date.ToString("yyyy-MM-dd"));
                    tracker.Update(runId, r => r.Errores.Add(
                        $"[{day.Date}] No se pudo crear paciente {patient.Identifier}"));
                    continue;
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
                FijarProximaVisita(patient, day.Date, rng);
                lock (_poolLock) _patientPool.Add(patient);
                tracker.Update(runId, r => r.PacientesCreados++);
            }

            // ── Pacientes recurrentes ─────────────────────────────────────────
            List<SimulatedPatient> poolSnapshot;
            lock (_poolLock) poolSnapshot = [.. _patientPool];

            // Excluir pacientes ya visitados hoy (nuevos del mismo día)
            var uuidsHoy = new HashSet<string>(
                poolSnapshot
                    .Where(p => p.VisitDatetime.Date == day.Date.ToDateTime(TimeOnly.MinValue).Date)
                    .Select(p => p.OpenMrsUuid));

            for (int i = 0; i < day.PacientesRecurrentes; i++)
            {
                if (poolSnapshot.Count == 0) break;

                // Elegibles: no atendidos hoy y que ya cumplieron su intervalo mínimo entre visitas.
                var disponibles = poolSnapshot
                    .Where(p => !uuidsHoy.Contains(p.OpenMrsUuid))
                    .Where(p => p.ProximoElegibleDesde is null || day.Date >= p.ProximoElegibleDesde.Value)
                    .ToList();
                if (disponibles.Count == 0) break;

                var base_ = disponibles[rng.Next(disponibles.Count)];
                uuidsHoy.Add(base_.OpenMrsUuid);
                var preferCommonRec = _epiSelector.RollPreferCommon(runCommonP);

                // Continuidad longitudinal: si el paciente ya arrastra una condición crónica, con alta
                // probabilidad esta visita es un CONTROL de esa misma condición (no un motivo nuevo).
                DiagnosticoEntry? dxSeguimiento = null;
                if (base_.CronicasActivas.Count > 0 &&
                    _epiSelector.RollSeguimientoCronico(_settings.SeguimientoCronicoProb))
                {
                    dxSeguimiento = base_.CronicasActivas[rng.Next(base_.CronicasActivas.Count)];
                }

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
                    AgeGroup      = base_.AgeGroup,
                    Address1      = base_.Address1,
                    City          = base_.City,
                    EsNuevo       = false,
                    // Compartir historial de órdenes y lista de problemas con el paciente original
                    OrderedConcepts = base_.OrderedConcepts,
                    ProblemListConcepts = base_.ProblemListConcepts,
                    // Heredar el médico de cabecera (asignado en la primera visita del paciente)
                    CabeceraLocationUuid = base_.CabeceraLocationUuid,
                    CabeceraProviderUuid = base_.CabeceraProviderUuid,
                    ClimaEstacion = estacion,
                    TempAmbienteC = tempC,
                    // Control de crónica → misma categoría; si no, se elige una nueva (motivo agudo).
                    Categoria     = dxSeguimiento?.Categoria
                                    ?? _epiSelector.SelectCategoria(base_.AgeGroup, base_.Gender, estacion, preferCommonRec),
                    VisitDatetime = _schedule.GenerateVisitTime(day.Date)
                };
                recurrente.Diagnostico = dxSeguimiento
                    ?? _epiSelector.SelectDiagnostico(
                        recurrente.Categoria, recurrente.AgeGroup, recurrente.Gender, estacion, preferCommonRec);
                recurrente.Comorbilidades = recurrente.Diagnostico is null
                    ? []
                    : _epiSelector.SelectComorbilidades(recurrente.Diagnostico, recurrente.AgeGroup, recurrente.Gender, estacion);

                await ProcesarVisitaAsync(recurrente, day.Date, tracker, runId, ct);

                // Persistir en el paciente original cualquier crónica nueva surgida en esta visita.
                RegistrarCronicas(base_, recurrente);
                FijarProximaVisita(base_, day.Date, rng);
            }

            diasProcesados++;
            var pct = (int)(diasProcesados * 100.0 / Math.Max(diasConPacientes, 1));
            tracker.Update(runId, r => { r.Porcentaje = pct; r.DiasProcesados = diasProcesados; });
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
    /// Fija en el paciente del pool la fecha más temprana de su próxima visita, imponiendo el intervalo
    /// mínimo entre visitas (crónico = control mensual/trimestral; agudo = 1–3 semanas).
    /// </summary>
    private void FijarProximaVisita(SimulatedPatient poolPatient, DateOnly visita, Random rng)
    {
        poolPatient.ProximoElegibleDesde = RecurrenceScheduler.ProximaFechaElegible(
            visita, poolPatient.CronicasActivas.Count > 0, rng, _settings.Recurrence);
    }

    private async Task ProcesarVisitaAsync(
        SimulatedPatient patient,
        DateOnly date,
        SeedProgressTracker tracker,
        Guid runId,
        CancellationToken ct)
    {
        // Consultorio + médico de esta visita: nuevos estrenan cabecera; recurrentes vuelven a la suya
        // con alta probabilidad (o caen con otro médico).
        _clinicResources.AssignVisit(patient);

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
        await _consultaSeeder.SeedAsync(patient, ct);
        await _conditionSeeder.SeedAsync(patient, ct);
        await _labOrderSeeder.SeedAsync(patient, ct);
        await _prescriptionSeeder.SeedAsync(patient, ct);
        await _visitCloseSeeder.SeedAsync(patient, ct);
    }
}
