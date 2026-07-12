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

    public async Task RunAsync(Guid runId, SeedProgressTracker tracker, CancellationToken ct)
    {
        var days             = _schedule.Generate();
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
            _clinicResources.ActivarMedicosDelDia(rng);

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
                RegistrarEpisodioAgudo(patient, patient, day.Date, fueControlAgudo: false);
                FijarProximaVisita(patient, patient, day.Date, rng);
                _patientPool.Add(patient);
                tracker.Update(runId, r => r.PacientesCreados++);
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
                var preferCommonRec = _epiSelector.RollPreferCommon(runCommonP);

                // Continuidad longitudinal: si el paciente ya arrastra una condición crónica, con alta
                // probabilidad esta visita es un CONTROL de esa misma condición (no un motivo nuevo).
                // Si no, y tiene un episodio AGUDO abierto dentro de su ventana, con alta probabilidad
                // vuelve por ese mismo dx (control/mejoría) — el de neumonía no regresa con dermatitis.
                DiagnosticoEntry? dxSeguimiento = null;
                var esControlAgudo = false;
                if (base_.CronicasActivas.Count > 0 &&
                    _epiSelector.RollSeguimientoCronico(_settings.SeguimientoCronicoProb))
                {
                    dxSeguimiento = base_.CronicasActivas[rng.Next(base_.CronicasActivas.Count)];
                }
                else if (base_.UltimoDxAgudo is not null &&
                         EpidemiologySelector.EpisodioAgudoVigente(
                             base_.FechaUltimoDxAgudo, day.Date, _settings.VentanaSeguimientoAgudoDias) &&
                         _epiSelector.RollSeguimientoAgudo(_settings.SeguimientoAgudoProb))
                {
                    dxSeguimiento = base_.UltimoDxAgudo;
                    esControlAgudo = true;
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
                    // Recalcular la franja de edad a la fecha de ESTA visita (la edad avanza con el tiempo
                    // simulado), en vez de copiar la de la primera visita.
                    AgeGroup      = PatientProfileGenerator.GrupoEdad(base_.BirthDate, day.Date),
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
                RegistrarEpisodioAgudo(base_, recurrente, day.Date, esControlAgudo);
                FijarProximaVisita(base_, recurrente, day.Date, rng);
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
    /// fecha (la agenda gobierna el retorno). Si no, se impone solo el intervalo mínimo entre visitas
    /// (crónico = control mensual/trimestral; agudo = 1–3 semanas) y no queda cita que priorizar.
    /// </summary>
    private void FijarProximaVisita(
        SimulatedPatient poolPatient, SimulatedPatient visitPatient, DateOnly visita, Random rng)
    {
        if (visitPatient.FechaSeguimiento is { } fs)
        {
            var cita = DateOnly.FromDateTime(fs);
            poolPatient.ProximaCita          = cita;
            poolPatient.ProximoElegibleDesde = cita;
        }
        else
        {
            poolPatient.ProximaCita          = null;
            poolPatient.ProximoElegibleDesde = RecurrenceScheduler.ProximaFechaElegible(
                visita, poolPatient.CronicasActivas.Count > 0, rng, _settings.Recurrence);
        }
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
        // El paciente "llegó": resolver sus citas pendientes (Completed si cae cerca, Missed si venció)
        await _appointmentSeeder.ResolverCitasAsync(patient, ct);
        await _consultaSeeder.SeedAsync(patient, ct);
        await _conditionSeeder.SeedAsync(patient, ct);
        await _programSeeder.SeedAsync(patient, ct);
        // "Ya llegó el resultado": entregar los labs que quedaron pendientes de visitas anteriores
        await _labOrderSeeder.ProcesarPendientesAsync(patient, ct);
        await _labOrderSeeder.SeedAsync(patient, ct);
        await _prescriptionSeeder.SeedAsync(patient, ct);
        // Agendar la cita real del seguimiento decidido en la consulta (si lo hubo)
        await _appointmentSeeder.SeedAsync(patient, ct);
        await _visitCloseSeeder.SeedAsync(patient, ct);
    }
}
