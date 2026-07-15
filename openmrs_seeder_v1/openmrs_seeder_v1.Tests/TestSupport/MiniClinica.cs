using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests.TestSupport;

/// <summary>Mandos del harness: fracciones clínicas y los interruptores de sabotaje.</summary>
public sealed class OpcionesMiniClinica
{
    /// <summary>Fracción de diagnósticos primarios que son crónicos (la corrida real ronda 0,35).</summary>
    public double FraccionCronicos { get; init; } = 0.35;

    /// <summary>Fracción de diagnósticos con severidad "grave".</summary>
    public double FraccionGrave { get; init; } = 0.10;

    /// <summary>Prob. de que a un recurrente lo atienda su médico de cabecera (media de la banda real 0,70-0,90).</summary>
    public double ProbCabecera { get; init; } = 0.80;

    /// <summary>false = no se agendan citas reales (feature Appointments apagada) → la ley L2 no aplica.</summary>
    public bool CitasActivas { get; init; } = true;

    /// <summary>
    /// <b>SABOTAJE — el bug histórico.</b> Si se fija (p.ej. 0,30), el día vuelve a repartirse con el cupo:
    /// <c>total = altas / (1 − cupo)</c> y los retornos que no caben en el residuo <b>se desplazan sin
    /// atender</b> (sus citas vencen en Missed). Es exactamente el reparto que tiró a la basura la mitad
    /// de la agenda (14.025 citas Missed) y dejó al 65 % de los crónicos sin volver jamás. La suite lo usa
    /// para demostrar que, si alguien lo reintroduce, las leyes se ponen rojas — cosa que los 380 tests de
    /// piezas de la época no consiguieron.
    /// </summary>
    public double? CupoRecurrentes { get; init; }
}

/// <summary>Lo que deja una corrida del harness: el estado agregado que juzgan las leyes.</summary>
public sealed record ResultadoMiniClinica(
    RunStats Stats,
    List<SimulatedPatient> Pool,
    IReadOnlyList<Ley> Leyes,
    IReadOnlyList<Ley> Rotas,
    double P,
    double Q)
{
    public Ley Ley(string codigo) => Leyes.Single(l => l.Codigo == codigo);
    public bool Rota(string codigo) => Rotas.Any(l => l.Codigo == codigo);
}

/// <summary>
/// <b>La clínica en memoria</b>: ejecuta la mecánica del día N días simulados usando los MISMOS seams
/// puros que la producción — sin OpenMRS, sin red, sin reloj — y al final pasa las leyes
/// (<see cref="Invariantes.Evaluar"/>). Es el test del sistema que faltó cuando Bass rompió la
/// continuidad longitudinal con 380 tests de piezas en verde.
///
/// <para><b>Fidelidad</b>: cada paso replica el bucle de <c>SeedOrchestrator.RunAsync</c>. Si el
/// orquestador cambia, esta tabla es el contrato a revisar (líneas de SeedOrchestrator.cs, jul 2026):</para>
/// <code>
/// Paso del día                          SeedOrchestrator.cs          Seam de producción reusado
/// ─────────────────────────────────     ─────────────────────        ─────────────────────────────────
/// ¿día abierto?                         Abierta() (129-131)          DailyScheduleGenerator.PesoDelDia
/// estado del pool (A, S, peso, λ)       174-177                      BassGrowthModel.PacientesActivos /
///                                                                    RecurrentesActivosSatisfechos / Lambda
/// retornos PRIMERO (sin cupo)           182-189                      RecurrentSelector.Seleccionar
/// altas con lo que quede de aforo       193-199                      BassGrowthModel.NuevosDelDia /
///                                                                    CapacidadDelDia (recorta SOLO altas)
/// total y techo                         201-202                      —
/// motivo del recurrente                 292-297                      SeedOrchestrator.DxDeControl
/// resolver citas del que vuelve         (ProcesarVisitaAsync)        AppointmentSeeder.ClasificarCitas
/// crónicas / episodio agudo             RegistrarCronicas (478),     réplica fiel (~15 líneas)
///                                       RegistrarEpisodioAgudo (493)
/// agendar control y próxima elegib.     FijarProximaVisita (553)     SeguimientoPolicy.Probabilidad +
///                                                                    RecurrenceScheduler.ProximaFechaElegible
/// calificación de la visita             RegistrarCalificacion (529)  SatisfaccionPolicy.Calificar/EsInsatisfecho
/// cierre del día                        377-383                      RunStats.RegistrarDiaSimulado
/// barrido final de la agenda            SweepMissedAsync (415)       AppointmentSeeder.CitasVencidas
/// veredicto                             Program.cs etapa 4/5         Invariantes.Evaluar / Rotas
/// </code>
///
/// <para>Lo único que NO se replica son los POST a OpenMRS (aquí <c>VisitUuid</c> se marca sintético) y
/// los seeders clínicos internos de la visita (vitals, labs, prescripciones), que no afectan al volumen
/// ni a la recurrencia — el terreno que las leyes vigilan.</para>
/// </summary>
public static class MiniClinica
{
    public static ResultadoMiniClinica Correr(SimulationSettings sim, OpcionesMiniClinica? opciones = null)
    {
        var op = opciones ?? new OpcionesMiniClinica();
        var cr = sim.Crecimiento;
        var re = sim.Recurrence;

        var plan = new DailyScheduleGenerator(sim).Generate();

        // Los mismos offsets de RNG que el orquestador (RandomSeed +10/+19/+20) y uno propio para el
        // "catálogo" sintético de diagnósticos (+30, un flujo que en producción pone EpidemiologySelector).
        var rng         = new Random(sim.RandomSeed + 10);
        var rngSat      = new Random(sim.RandomSeed + 19);
        var rngLlegadas = new Random(sim.RandomSeed + 20);
        var rngDx       = new Random(sim.RandomSeed + 30);

        var p = BassGrowthModel.CoeficienteInnovacion(sim.PacientesPorDiaMedio, cr.PoblacionCaptacion);
        var q = BassGrowthModel.CoeficienteImitacion(plan, sim);
        var probRetornoEspontaneo = RecurrentSelector.ProbRetornoEspontaneoDiaria(re);

        var pool  = new List<SimulatedPatient>();
        var stats = new RunStats();
        var siguienteId = 0;

        bool Abierta(DailySchedule d) => cr.Enabled
            ? DailyScheduleGenerator.PesoDelDia(d.Date.DayOfWeek, sim.WeekdayWeights) > 0
            : d.TotalPatients > 0;

        // El catálogo sintético: crónico / agudo / grave en sus fracciones. Instancias NUEVAS por visita
        // (como el selector real), la dedup de crónicas es por CielUuid.
        DiagnosticoEntry SortearDx()
        {
            var esCronico = rngDx.NextDouble() < op.FraccionCronicos;
            var esGrave   = rngDx.NextDouble() < op.FraccionGrave;
            var n         = rngDx.Next(esCronico ? 8 : 40);   // 8 crónicas y 40 agudas distintas
            return Factorias.Dx(
                uuid:      (esCronico ? "cronica-" : "aguda-") + n,
                cronica:   esCronico,
                severidad: esGrave ? "grave" : "moderado",
                categoria: esCronico ? "cardiovascular" : "respiratorio");
        }

        // ── Réplicas fieles de la contabilidad post-visita del orquestador ─────────────────────────

        void RegistrarCronicas(SimulatedPatient poolPatient, DiagnosticoEntry dx)
        {
            if (!dx.EsCronica) return;
            if (poolPatient.CronicasActivas.Any(c => c.CielUuid == dx.CielUuid)) return;
            poolPatient.CronicasActivas.Add(dx);
        }

        void RegistrarEpisodioAgudo(
            SimulatedPatient poolPatient, DiagnosticoEntry dx, DateOnly visita, bool fueControlAgudo)
        {
            if (fueControlAgudo)
            {
                poolPatient.UltimoDxAgudo = null;
                poolPatient.FechaUltimoDxAgudo = null;
                return;
            }
            if (dx.EsCronica) return;
            poolPatient.UltimoDxAgudo = dx;
            poolPatient.FechaUltimoDxAgudo = visita;
        }

        void FijarProximaVisita(SimulatedPatient poolPatient, DiagnosticoEntry dx, DateOnly visita)
        {
            poolPatient.UltimaVisita = visita;
            poolPatient.Visitas++;

            // ConsultaSeeder: ¿se agenda control? La probabilidad depende del cuadro (crónico > grave > resto)
            var probSeguimiento = SeguimientoPolicy.Probabilidad([dx], sim.ReferralProbabilities);
            if (rng.NextDouble() < probSeguimiento)
            {
                var cita = RecurrenceScheduler.ProximaFechaElegible(visita, dx.EsCronica, rng, re);
                poolPatient.ProximaCita          = cita;
                poolPatient.ProximoElegibleDesde = cita;
                poolPatient.MotivoProximaCita    = dx;
                if (op.CitasActivas)
                    poolPatient.CitasPendientes.Add(new CitaPendiente(
                        $"cita-{poolPatient.OpenMrsUuid}-{poolPatient.Visitas}",
                        cita.ToDateTime(new TimeOnly(9, 0))));
            }
            else
            {
                poolPatient.ProximaCita          = null;
                poolPatient.MotivoProximaCita    = null;
                poolPatient.ProximoElegibleDesde = RecurrenceScheduler.ProximaFechaElegible(
                    visita, poolPatient.CronicasActivas.Count > 0, rng, re);
            }
        }

        void RegistrarCalificacion(
            SimulatedPatient poolPatient, DiagnosticoEntry dx, bool esNuevo, bool acudioACita, int pacientesDelDia)
        {
            if (!sim.Satisfaccion.Enabled) return;
            var ctx = new ContextoVisita(
                AtendidoPorCabecera: !esNuevo && rngSat.NextDouble() < op.ProbCabecera,
                AcudioACita:         acudioACita,
                CuadroGrave:         dx.Severidad.Equals("grave", StringComparison.OrdinalIgnoreCase),
                PacientesDelDia:     pacientesDelDia);
            var nota = SatisfaccionPolicy.Calificar(ctx, sim.Satisfaccion, rngSat);
            poolPatient.Calificaciones.Add(nota);
            poolPatient.Insatisfecho = SatisfaccionPolicy.EsInsatisfecho(
                poolPatient.Calificaciones, sim.Satisfaccion.UmbralSatisfaccion);
            stats.RegistrarCalificacion(nota);
        }

        // ── El bucle del día (espejo de SeedOrchestrator.RunAsync, 165-405) ────────────────────────

        foreach (var day in plan)
        {
            if (!Abierta(day)) continue;

            var activos     = BassGrowthModel.PacientesActivos(pool, day.Date, cr);
            var satisfechos = BassGrowthModel.RecurrentesActivosSatisfechos(pool, day.Date, cr);
            var pesoDia     = DailyScheduleGenerator.PesoDelDia(day.Date.DayOfWeek, sim.WeekdayWeights);
            var lambda      = BassGrowthModel.Lambda(p, q, cr.PoblacionCaptacion, activos, satisfechos);

            // 1) Retornos PRIMERO, sobre el pool sin las altas de hoy (dedup por construcción).
            var elegibles = pool
                .Where(pac => pac.ProximoElegibleDesde is null || day.Date >= pac.ProximoElegibleDesde.Value)
                .ToList();

            var seleccionados = RecurrentSelector.Seleccionar(
                elegibles, day.Date,
                sim.Appointments.ToleranciaDias, sim.Appointments.AsistenciaProb,
                cr.AsistenciaProbInsatisfecho, probRetornoEspontaneo, cr.VentanaActividadDias, rng);

            // 2) Las altas, con lo que quede de aforo (el aforo SOLO muerde a las altas).
            var demandaNuevos = cr.Enabled
                ? BassGrowthModel.NuevosDelDia(lambda, pesoDia, rngLlegadas)
                : day.NuevosPacientes;

            // ── SABOTAJE: el reparto por cupo del bug histórico ─────────────────────────────────────
            var retornosDesplazados = 0;
            if (op.CupoRecurrentes is { } cupoPct)
            {
                var totalPlan    = (int)Math.Round(demandaNuevos / Math.Max(0.05, 1 - cupoPct));
                var cupoRetornos = Math.Max(0, totalPlan - demandaNuevos);
                if (seleccionados.Count > cupoRetornos)
                {
                    retornosDesplazados = seleccionados.Count - cupoRetornos;
                    seleccionados       = seleccionados.Take(cupoRetornos).ToList();
                }
            }

            var aforo            = BassGrowthModel.CapacidadDelDia(sim, pesoDia);
            var cupoNuevos       = Math.Max(0, Math.Min(demandaNuevos, aforo - seleccionados.Count));
            var nuevosRechazados = demandaNuevos - cupoNuevos;

            var totalDelDia = cupoNuevos + seleccionados.Count;
            var topoElTecho = totalDelDia >= cr.PacientesPorDiaMax;

            stats.RecurrentesSatisfechosFinal = satisfechos;
            stats.PacientesActivosFinal       = activos;

            // ── Pacientes nuevos ────────────────────────────────────────────────────────────────────
            for (var i = 0; i < cupoNuevos; i++)
            {
                var dx = SortearDx();
                var nuevo = new SimulatedPatient
                {
                    OpenMrsUuid = $"p-{siguienteId++}",
                    EsNuevo     = true,
                    VisitUuid   = "visita-sintetica",
                    Diagnostico = dx,
                };
                stats.RegistrarVisita(nuevo, day.Date);
                RegistrarCronicas(nuevo, dx);
                RegistrarEpisodioAgudo(nuevo, dx, day.Date, fueControlAgudo: false);
                FijarProximaVisita(nuevo, dx, day.Date);
                RegistrarCalificacion(nuevo, dx, esNuevo: true, acudioACita: false, totalDelDia);
                pool.Add(nuevo);
            }

            // ── Pacientes recurrentes (la demanda real del panel, ya elegida arriba) ────────────────
            foreach (var base_ in seleccionados)
            {
                var (dxSeguimiento, _, esControlAgudo) = SeedOrchestrator.DxDeControl(
                    base_, day.Date,
                    sim.Appointments.ToleranciaDias, sim.VentanaSeguimientoAgudoDias,
                    () => rng.NextDouble() < sim.SeguimientoCronicoProb,
                    () => rng.NextDouble() < sim.SeguimientoAgudoProb,
                    rng.Next);

                var acudioACita = RecurrentSelector.TieneCitaHoy(
                    base_, day.Date, sim.Appointments.ToleranciaDias);

                // Resolver la agenda del que vuelve (ProcesarVisitaAsync → ResolverCitasAsync).
                if (op.CitasActivas && base_.CitasPendientes.Count > 0)
                {
                    var fechaVisita = day.Date.ToDateTime(new TimeOnly(9, 0));
                    var (completar, perder, conservar) = AppointmentSeeder.ClasificarCitas(
                        base_.CitasPendientes, fechaVisita, sim.Appointments.ToleranciaDias);
                    foreach (var _ in completar) stats.RegistrarCitaResuelta(cumplida: true);
                    foreach (var _ in perder)    stats.RegistrarCitaResuelta(cumplida: false);
                    base_.CitasPendientes = conservar;
                }

                var dx = dxSeguimiento ?? SortearDx();
                var visita = new SimulatedPatient
                {
                    OpenMrsUuid = base_.OpenMrsUuid,
                    EsNuevo     = false,
                    VisitUuid   = "visita-sintetica",
                    Diagnostico = dx,
                };
                stats.RegistrarVisita(visita, day.Date);
                RegistrarCronicas(base_, dx);
                RegistrarEpisodioAgudo(base_, dx, day.Date, esControlAgudo);
                FijarProximaVisita(base_, dx, day.Date);
                RegistrarCalificacion(base_, dx, esNuevo: false, acudioACita, totalDelDia);
            }

            // Sin relleno de cupo: los retornos son los que son y las altas, las que caben.
            stats.RegistrarDiaSimulado(
                atendidos:           totalDelDia,
                nuevosRechazados:    nuevosRechazados,
                retornosDesplazados: retornosDesplazados,
                topoElTecho:         topoElTecho);
        }

        // ── Cierre de agenda: citas vencidas de quien nunca volvió → Missed ────────────────────────
        var endDate = DateOnly.FromDateTime(sim.EndDate);
        foreach (var pac in pool)
        {
            if (pac.CitasPendientes.Count == 0) continue;
            var vencidas = AppointmentSeeder.CitasVencidas(
                pac.CitasPendientes, endDate, sim.Appointments.ToleranciaDias);
            foreach (var _ in vencidas) stats.RegistrarCitaResuelta(cumplida: false);
            pac.CitasPendientes = pac.CitasPendientes.Except(vencidas).ToList();
        }

        var leyes = Invariantes.Evaluar(stats, pool, sim);
        return new ResultadoMiniClinica(stats, pool, leyes, Invariantes.Rotas(leyes), p, q);
    }
}
