using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class SeedOrchestrator
{
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

    private readonly List<SimulatedPatient> _patientPool = [];
    private readonly Lock _poolLock = new();
    private readonly ILogger<SeedOrchestrator> _logger;

    public SeedOrchestrator(
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
        ILogger<SeedOrchestrator> logger)
    {
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
        _logger             = logger;
    }

    public async Task RunAsync(Guid runId, SeedProgressTracker tracker, CancellationToken ct)
    {
        var days             = _schedule.Generate();
        var diasConPacientes = days.Count(d => d.TotalPatients > 0);
        var rng              = new Random();

        _logger.LogInformation("[Orchestrator] Iniciando run {RunId} — {Dias} días con pacientes",
            runId, diasConPacientes);

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
                patient.Categoria   = _epiSelector.SelectCategoria(patient.AgeGroup, patient.Gender, estacion);
                patient.Diagnostico = _epiSelector.SelectDiagnostico(patient.Categoria, patient.AgeGroup, patient.Gender, estacion);
                patient.Comorbilidades = patient.Diagnostico is null
                    ? []
                    : _epiSelector.SelectComorbilidades(patient.Diagnostico, patient.AgeGroup, patient.Gender, estacion);
                patient.VisitDatetime = _schedule.GenerateVisitTime(day.Date);

                await ProcesarVisitaAsync(patient, day.Date, tracker, runId, ct);

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

                var disponibles = poolSnapshot.Where(p => !uuidsHoy.Contains(p.OpenMrsUuid)).ToList();
                if (disponibles.Count == 0) break;

                var base_ = disponibles[rng.Next(disponibles.Count)];
                uuidsHoy.Add(base_.OpenMrsUuid);
                var recurrente = new SimulatedPatient
                {
                    Identifier    = base_.Identifier,
                    OpenMrsUuid   = base_.OpenMrsUuid,
                    GivenName     = base_.GivenName,
                    FamilyName    = base_.FamilyName,
                    Gender        = base_.Gender,
                    BirthDate     = base_.BirthDate,
                    AgeGroup      = base_.AgeGroup,
                    Address1      = base_.Address1,
                    City          = base_.City,
                    EsNuevo       = false,
                    // Compartir historial de órdenes con el paciente original (mismo objeto por referencia)
                    OrderedConcepts = base_.OrderedConcepts,
                    ClimaEstacion = estacion,
                    TempAmbienteC = tempC,
                    // Diagnóstico puede cambiar en visita recurrente
                    Categoria     = _epiSelector.SelectCategoria(base_.AgeGroup, base_.Gender, estacion),
                    VisitDatetime = _schedule.GenerateVisitTime(day.Date)
                };
                recurrente.Diagnostico = _epiSelector.SelectDiagnostico(
                    recurrente.Categoria, recurrente.AgeGroup, recurrente.Gender, estacion);
                recurrente.Comorbilidades = recurrente.Diagnostico is null
                    ? []
                    : _epiSelector.SelectComorbilidades(recurrente.Diagnostico, recurrente.AgeGroup, recurrente.Gender, estacion);

                await ProcesarVisitaAsync(recurrente, day.Date, tracker, runId, ct);
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

    private async Task ProcesarVisitaAsync(
        SimulatedPatient patient,
        DateOnly date,
        SeedProgressTracker tracker,
        Guid runId,
        CancellationToken ct)
    {
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
        await _labOrderSeeder.SeedAsync(patient, ct);
        await _prescriptionSeeder.SeedAsync(patient, ct);
        await _visitCloseSeeder.SeedAsync(patient, ct);
    }
}
