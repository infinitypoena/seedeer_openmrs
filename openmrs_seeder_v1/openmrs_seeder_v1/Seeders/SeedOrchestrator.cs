using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class SeedOrchestrator
{
    private readonly DailyScheduleGenerator _schedule;
    private readonly PatientProfileGenerator _profiler;
    private readonly EpidemiologySelector _epiSelector;
    private readonly PatientSeeder _patientSeeder;
    private readonly AllergySeeder _allergySeeder;
    private readonly VisitSeeder _visitSeeder;
    private readonly VitalsSeeder _vitalsSeeder;
    private readonly ConsultaSeeder _consultaSeeder;
    private readonly LabOrderSeeder _labOrderSeeder;
    private readonly PrescriptionSeeder _prescriptionSeeder;
    private readonly VisitCloseSeeder _visitCloseSeeder;

    // Pool de pacientes creados en este run para reutilización como recurrentes
    private readonly List<SimulatedPatient> _patientPool = [];
    private readonly Lock _poolLock = new();

    public SeedOrchestrator(
        DailyScheduleGenerator schedule,
        PatientProfileGenerator profiler,
        EpidemiologySelector epiSelector,
        PatientSeeder patientSeeder,
        AllergySeeder allergySeeder,
        VisitSeeder visitSeeder,
        VitalsSeeder vitalsSeeder,
        ConsultaSeeder consultaSeeder,
        LabOrderSeeder labOrderSeeder,
        PrescriptionSeeder prescriptionSeeder,
        VisitCloseSeeder visitCloseSeeder)
    {
        _schedule          = schedule;
        _profiler          = profiler;
        _epiSelector       = epiSelector;
        _patientSeeder     = patientSeeder;
        _allergySeeder     = allergySeeder;
        _visitSeeder       = visitSeeder;
        _vitalsSeeder      = vitalsSeeder;
        _consultaSeeder    = consultaSeeder;
        _labOrderSeeder    = labOrderSeeder;
        _prescriptionSeeder = prescriptionSeeder;
        _visitCloseSeeder  = visitCloseSeeder;
    }

    public async Task RunAsync(Guid runId, SeedProgressTracker tracker, CancellationToken ct)
    {
        var days             = _schedule.Generate();
        var diasConPacientes = days.Count(d => d.TotalPatients > 0);
        var rng              = new Random();

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

            tracker.Update(runId, r => r.FechaActual = day.Date.ToString("yyyy-MM-dd"));

            // ── Pacientes nuevos ──────────────────────────────────────────────
            for (int i = 0; i < day.NuevosPacientes; i++)
            {
                var patient = _profiler.GenerateNew();
                var uuid    = await _patientSeeder.CreateAsync(patient, ct);
                if (uuid is null)
                {
                    tracker.Update(runId, r => r.Errores.Add(
                        $"[{day.Date}] No se pudo crear paciente {patient.Identifier}"));
                    continue;
                }
                patient.OpenMrsUuid = uuid;

                await _allergySeeder.SeedAsync(patient, ct);

                patient.Categoria   = _epiSelector.SelectCategoria(patient.AgeGroup, patient.Gender);
                patient.Diagnostico = _epiSelector.SelectDiagnostico(patient.Categoria, patient.AgeGroup, patient.Gender);
                patient.VisitDatetime = _schedule.GenerateVisitTime(day.Date);

                await ProcesarVisitaAsync(patient, day.Date, tracker, runId, ct);

                lock (_poolLock) _patientPool.Add(patient);
                tracker.Update(runId, r => r.PacientesCreados++);
            }

            // ── Pacientes recurrentes ─────────────────────────────────────────
            List<SimulatedPatient> poolSnapshot;
            lock (_poolLock) poolSnapshot = [.. _patientPool];

            for (int i = 0; i < day.PacientesRecurrentes; i++)
            {
                if (poolSnapshot.Count == 0) break;

                var base_ = poolSnapshot[rng.Next(poolSnapshot.Count)];
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
                    // Diagnóstico puede cambiar en visita recurrente
                    Categoria     = _epiSelector.SelectCategoria(base_.AgeGroup, base_.Gender),
                    VisitDatetime = _schedule.GenerateVisitTime(day.Date)
                };
                recurrente.Diagnostico = _epiSelector.SelectDiagnostico(
                    recurrente.Categoria, recurrente.AgeGroup, recurrente.Gender);

                await ProcesarVisitaAsync(recurrente, day.Date, tracker, runId, ct);
            }

            diasProcesados++;
            var pct = (int)(diasProcesados * 100.0 / Math.Max(diasConPacientes, 1));
            tracker.Update(runId, r => { r.Porcentaje = pct; r.DiasProcesados = diasProcesados; });
        }

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
