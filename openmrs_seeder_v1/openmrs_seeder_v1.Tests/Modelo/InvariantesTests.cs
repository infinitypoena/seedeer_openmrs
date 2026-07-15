using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Modelo;

/// <summary>
/// Las leyes de la simulación, <b>ley por ley</b>: cada umbral, su caso que rompe, su caso que pasa y su
/// "no procede". El caso de regresión completo (la corrida rota de 3,5 años) y el tripwire L3 viven en
/// <c>Sistema/</c> — aquí se verifica la mecánica de <see cref="Invariantes"/> como pieza.
/// </summary>
public class InvariantesTests
{
    private static Ley Buscar(IReadOnlyList<Ley> leyes, string codigo) =>
        leyes.Single(l => l.Codigo == codigo);

    [Fact]
    public void UnaCorridaSanaConstruidaAMano_CumpleTodasLasLeyes()
    {
        var sim = Escenarios.SimObjetivo();

        var pool = new List<SimulatedPatient>();
        for (var i = 0; i < 900; i++)  pool.Add(Factorias.P(visitas: 5, cronico: true));   // el crónico vuelve
        for (var i = 0; i < 100; i++)  pool.Add(Factorias.P(visitas: 1, cronico: true));
        for (var i = 0; i < 1000; i++) pool.Add(Factorias.P(visitas: 3));
        for (var i = 0; i < 1000; i++) pool.Add(Factorias.P(visitas: 1));

        var stats = Estadisticas.Sembrar(
            porAnio: [(2023, 1200, 700), (2025, 500, 1800)],   // el panel MADURA: 37 % → 78 %
            citasCumplidas: 3000,
            citasPerdidas: 600,                                // 17 % de no-show: realista
            diasConAtencion: 900,
            diasEnElTecho: 0,
            atendidosPorDia: 25);                              // en su objetivo

        Assert.Empty(Invariantes.Rotas(Invariantes.Evaluar(stats, pool, sim)));
    }

    // ══ L1 · El crónico vuelve a su control ══════════════════════════════════════════════════════

    [Fact]
    public void L1_ElCronicoQueNoVuelve_RompeLaLey()
    {
        var sim = Escenarios.SimObjetivo();
        var pool = new List<SimulatedPatient>();
        for (var i = 0; i < 50; i++) pool.Add(Factorias.P(visitas: 4, cronico: true));   // 50 vuelven
        for (var i = 0; i < 50; i++) pool.Add(Factorias.P(visitas: 1, cronico: true));   // 50 abandonados

        var ley = Buscar(Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 100, 100)]), pool, sim), "L1");

        Assert.True(ley.Aplica);
        Assert.False(ley.Cumple);   // 50 % < 60 %
    }

    [Fact]
    public void L1_NoSeJuzgaEnUnaVentanaCorta()
    {
        // Una corrida de una semana no puede decir nada sobre el control trimestral de un hipertenso.
        var sim = Escenarios.SimObjetivo();
        sim.EndDate = sim.StartDate.AddDays(7);
        var pool = new List<SimulatedPatient> { Factorias.P(visitas: 1, cronico: true) };

        var ley = Buscar(Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 30, 0)]), pool, sim), "L1");

        Assert.False(ley.Aplica);
        Assert.Empty(Invariantes.Rotas([ley]));   // "no procede" NO es un suspenso
    }

    [Fact]
    public void L1_SinCronicosEnElPool_NoProcede()
    {
        var sim = Escenarios.SimObjetivo();
        var pool = Factorias.Pool(100, _ => Factorias.P(visitas: 1));

        Assert.False(Buscar(Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 100, 0)]), pool, sim), "L1").Aplica);
    }

    // ══ L2 · La agenda se honra ══════════════════════════════════════════════════════════════════

    [Fact]
    public void L2_LaMitadDeLaAgendaPerdida_RompeLaLey()
    {
        var sim = Escenarios.SimObjetivo();
        var leyes = Invariantes.Evaluar(
            Estadisticas.Sembrar([(2023, 100, 100)], citasCumplidas: 100, citasPerdidas: 100), [], sim);

        Assert.False(Buscar(leyes, "L2").Cumple);   // 50 % > 25 %
    }

    [Fact]
    public void L2_UnNoShowRealista_PasaLaLey()
    {
        var sim = Escenarios.SimObjetivo();
        var leyes = Invariantes.Evaluar(
            Estadisticas.Sembrar([(2023, 100, 100)], citasCumplidas: 80, citasPerdidas: 20), [], sim);

        Assert.True(Buscar(leyes, "L2").Cumple);    // 20 % ≤ 25 %
    }

    [Fact]
    public void L2_SinCitasResueltas_NoProcede()
    {
        // La feature de Appointments apagada no puede aprobar (ni suspender) la agenda.
        var sim = Escenarios.SimObjetivo();

        Assert.False(Buscar(Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 100, 100)]), [], sim), "L2").Aplica);
    }

    // ══ L4 · El panel madura ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void L4_ElMixCongelado_RompeLaLey()
    {
        // El síntoma exacto del cupo fijo: mismo porcentaje de recurrentes el primer año y el último.
        var sim = Escenarios.SimObjetivo();
        var leyes = Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 70, 30), (2025, 70, 30)]), [], sim);

        var ley = Buscar(leyes, "L4");
        Assert.True(ley.Aplica);
        Assert.False(ley.Cumple);
    }

    [Fact]
    public void L4_ElPanelQueMadura_PasaLaLey()
    {
        var sim = Escenarios.SimObjetivo();
        var leyes = Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 90, 10), (2025, 40, 60)]), [], sim);

        Assert.True(Buscar(leyes, "L4").Cumple);   // 10 % → 60 %
    }

    [Fact]
    public void L4_ConUnSoloAno_NoProcede()
    {
        var sim = Escenarios.SimObjetivo();

        Assert.False(Buscar(Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 70, 30)]), [], sim), "L4").Aplica);
    }

    // ══ L5 · La curva es una curva, no una pared ═════════════════════════════════════════════════

    [Fact]
    public void L5_LaClinicaPegadaAlTecho_RompeLaLey()
    {
        var sim = Escenarios.SimObjetivo();
        var leyes = Invariantes.Evaluar(
            Estadisticas.Sembrar([(2023, 100, 100)], diasConAtencion: 100, diasEnElTecho: 79), [], sim);

        Assert.False(Buscar(leyes, "L5").Cumple);   // 79 % > 10 %
    }

    [Fact]
    public void L5_ConElCrecimientoApagado_NoProcede()
    {
        var sim = Escenarios.SimObjetivo();
        sim.Crecimiento.Enabled = false;
        var leyes = Invariantes.Evaluar(
            Estadisticas.Sembrar([(2023, 100, 100)], diasConAtencion: 100, diasEnElTecho: 79), [], sim);

        Assert.False(Buscar(leyes, "L5").Aplica);
    }

    // ══ L6 · No se capta a más gente de la que vive en el área ═══════════════════════════════════

    [Fact]
    public void L6_CaptarAMasGenteDeLaQueViveEnElArea_RompeLaLey()
    {
        var sim = Escenarios.SimObjetivo(poblacion: 30000);

        // 31.610 pacientes distintos de un área de 30.000: el 105 % del barrio.
        var stats = Estadisticas.Sembrar([(2023, 31610, 0)]);

        Assert.False(Buscar(Invariantes.Evaluar(stats, [], sim), "L6").Cumple);
    }

    // ══ L7 · Un paciente no es un ticket ═════════════════════════════════════════════════════════

    [Fact]
    public void L7_ElPacienteDeUnaSolaVisita_RompeLaLey()
    {
        var sim = Escenarios.SimObjetivo();
        // 1.000 pacientes distintos, 1.450 visitas → 1,45 visitas/paciente (el número real de la corrida).
        var stats = Estadisticas.Sembrar([(2023, 1000, 450)]);

        var ley = Buscar(Invariantes.Evaluar(stats, [], sim), "L7");
        Assert.True(ley.Aplica);
        Assert.False(ley.Cumple);
    }

    [Fact]
    public void L7_NoSeJuzgaConMenosDeUnAno()
    {
        var sim = Escenarios.SimObjetivo();
        sim.EndDate = sim.StartDate.AddDays(180);

        Assert.False(Buscar(Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 1000, 450)]), [], sim), "L7").Aplica);
    }

    // ══ L8 · La clínica llega a donde se le pidió (y no más) ═════════════════════════════════════

    [Fact]
    public void L8_LaClinicaQueSePasaDelObjetivo_RompeLaLey()
    {
        var sim = Escenarios.SimObjetivo();   // objetivo 25
        var leyes = Invariantes.Evaluar(
            Estadisticas.Sembrar([(2023, 100, 100)], diasConAtencion: 100, atendidosPorDia: 45), [], sim);

        Assert.False(Buscar(leyes, "L8").Cumple);   // 45 contra 25: un 80 % por encima
    }

    [Fact]
    public void L8_LaClinicaQueAterrizaEnSuObjetivo_PasaLaLey()
    {
        var sim = Escenarios.SimObjetivo();
        var leyes = Invariantes.Evaluar(
            Estadisticas.Sembrar([(2023, 100, 100)], diasConAtencion: 100, atendidosPorDia: 24), [], sim);

        Assert.True(Buscar(leyes, "L8").Cumple);
    }

    [Fact]
    public void L8_NoSeJuzgaAntesDeQueLaRampaTermine()
    {
        // Exigirle la meseta a una corrida más corta que la memoria del boca a boca es un falso positivo.
        var sim = Escenarios.SimObjetivo(ventanaDias: 365);
        sim.EndDate = sim.StartDate.AddDays(200);

        var leyes = Invariantes.Evaluar(
            Estadisticas.Sembrar([(2023, 100, 100)], diasConAtencion: 100, atendidosPorDia: 10), [], sim);

        Assert.False(Buscar(leyes, "L8").Aplica);
    }

    // ══ Las leyes que se juzgan ANTES de sembrar (proyección de la etapa 2/5) ════════════════════

    [Fact]
    public void LaProyeccionAvisaDeUnaCurvaContraElMuro_AntesDeTocarOpenMRS()
    {
        // Vale más una advertencia en la etapa 2/5 que descubrir a las 6 horas que la clínica lleva dos
        // años y medio clavada contra el techo.
        var sim = Escenarios.SimObjetivo(max: 26);   // techo pegado al objetivo: la curva topa siempre
        var plan  = new DailyScheduleGenerator(sim).Generate();
        var curva = BassGrowthModel.Proyectar(plan, sim, q: 0.5);

        Assert.False(Buscar(Invariantes.EvaluarProyeccion(curva, sim), "L5").Cumple);
    }

    [Fact]
    public void LaProyeccionAvisaDeUnAreaDemasiadoPequena()
    {
        var sim = Escenarios.SimObjetivo(poblacion: 2000);   // el barrio no da para tres años de clínica
        var plan  = new DailyScheduleGenerator(sim).Generate();
        var curva = BassGrowthModel.Proyectar(plan, sim, q: 0.2);

        Assert.False(Buscar(Invariantes.EvaluarProyeccion(curva, sim), "L6").Cumple);
    }
}
