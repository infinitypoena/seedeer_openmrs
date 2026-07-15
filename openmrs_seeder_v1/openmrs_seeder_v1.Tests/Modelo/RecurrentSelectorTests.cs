using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Modelo;

public class RecurrentSelectorTests
{
    private static readonly DateOnly Hoy = new(2024, 6, 10);
    private const int Ventana = 365;

    private static SimulatedPatient P(
        string uuid, DateOnly? cita = null, bool insatisfecho = false, int diasDesdeUltimaVisita = 30) =>
        new()
        {
            OpenMrsUuid  = uuid,
            ProximaCita  = cita,
            Insatisfecho = insatisfecho,
            Visitas      = 1,
            UltimaVisita = Hoy.AddDays(-diasDesdeUltimaVisita)
        };

    /// <summary>Selección sin retorno espontáneo: solo la agenda (aísla la vía de la cita).</summary>
    private static List<SimulatedPatient> SoloCitas(
        IReadOnlyList<SimulatedPatient> elegibles, double asistenciaProb, Random rng,
        double asistenciaInsatisfecho = 0.0) =>
        RecurrentSelector.Seleccionar(
            elegibles, Hoy, toleranciaDias: 3, asistenciaProb, asistenciaInsatisfecho,
            probRetornoEspontaneo: 0, ventanaActividadDias: Ventana, rng);

    // ── La agenda: TODA cita cumplida se atiende. No hay cupo que la estrangule. ───────────────────

    [Fact]
    public void TodasLasCitasDeHoySeAtienden_NoCompitenPorUnHueco()
    {
        // ⚠️ LA regresión de este arreglo. Antes esto pasaba por un cupo (30 % del volumen del día) y las
        // citas sobrantes vencían solas: 14.025 Missed contra 14.115 Completed en la corrida de 3,5 años,
        // con el 65 % de los crónicos sin volver jamás a un control.
        var citados = Enumerable.Range(0, 40).Select(i => P($"c{i}", Hoy)).ToList();

        var sel = SoloCitas(citados, asistenciaProb: 1.0, new Random(1));

        Assert.Equal(40, sel.Count);
    }

    [Fact]
    public void CitaDentroDeTolerancia_Cuenta_FueraDeTolerancia_No()
    {
        var dentro = P("dentro", Hoy.AddDays(3));    // dentro de ±3
        var fuera  = P("fuera",  Hoy.AddDays(10));   // aún no le toca

        var sel = SoloCitas([fuera, dentro], asistenciaProb: 1.0, new Random(2));

        Assert.Single(sel);
        Assert.Equal("dentro", sel[0].OpenMrsUuid);
    }

    [Fact]
    public void ElNoShow_NoSeAtiende_YSuCitaQuedaPendiente()
    {
        var conCita = P("cita", Hoy);

        // Asistencia 0 → no viene. Su cita se resolverá como Missed: es un no-show DE VERDAD, no una
        // víctima del aforo.
        Assert.Empty(SoloCitas([conCita], asistenciaProb: 0.0, new Random(3)));
    }

    [Fact]
    public void ElNoShow_NoSeCuelaPorLaViaEspontanea()
    {
        // Si no acudió a su cita, no puede aparecer hoy "por su cuenta": sería contarlo dos veces y su
        // cita quedaría colgada.
        var conCita = P("cita", Hoy);

        var sel = RecurrentSelector.Seleccionar(
            [conCita], Hoy, toleranciaDias: 3, asistenciaProb: 0.0, asistenciaProbInsatisfecho: 0.0,
            probRetornoEspontaneo: 1.0, ventanaActividadDias: Ventana, new Random(4));

        Assert.Empty(sel);
    }

    // ── El retorno espontáneo: "coger a cualquiera de la lista", pero como TASA ────────────────────

    [Fact]
    public void ElPacienteDelPanelVuelvePorSuCuenta_SinNecesidadDeCita()
    {
        // La vía que hace que el padrón de pacientes SE USE. Antes solo existía como "relleno de cupo", y
        // como las citas se comían el cupo entero, no llegó a ejecutarse ni una vez en 3,5 años.
        var pool = Enumerable.Range(0, 200).Select(i => P($"p{i}")).ToList();

        var sel = RecurrentSelector.Seleccionar(
            pool, Hoy, toleranciaDias: 3, asistenciaProb: 1.0, asistenciaProbInsatisfecho: 0.0,
            probRetornoEspontaneo: 0.10, ventanaActividadDias: Ventana, new Random(5));

        // ~10 % de 200: no se comprueba el número exacto, sino que la tasa se aplica de verdad.
        Assert.InRange(sel.Count, 8, 35);
    }

    [Fact]
    public void LaDemandaEspontaneaCreceConElPanel()
    {
        // La propiedad que hace que la fracción de recurrentes MADURE: con el doble de panel activo, el
        // doble de consulta espontánea. Con el cupo fijo, el mix era una constante para siempre.
        int Retornos(int tamañoDelPool)
        {
            var pool = Enumerable.Range(0, tamañoDelPool).Select(i => P($"p{i}")).ToList();
            return RecurrentSelector.Seleccionar(
                pool, Hoy, 3, 1.0, 0.0, probRetornoEspontaneo: 0.05, ventanaActividadDias: Ventana,
                new Random(6)).Count;
        }

        Assert.True(Retornos(1000) > Retornos(200) * 3);
    }

    [Fact]
    public void ElQueSeAlejoDeLaClinica_YaNoVuelveSolo()
    {
        // Fuera de la ventana de actividad ya no es cliente: habría que volver a captarlo. Sin esto, la
        // demanda espontánea crecería sin fin sobre un padrón que solo acumula.
        var activo = P("activo",  diasDesdeUltimaVisita: 364);
        var ido    = P("ido",     diasDesdeUltimaVisita: 366);

        var sel = RecurrentSelector.Seleccionar(
            [activo, ido], Hoy, 3, 1.0, 0.0,
            probRetornoEspontaneo: 1.0, ventanaActividadDias: Ventana, new Random(7));

        Assert.Single(sel);
        Assert.Equal("activo", sel[0].OpenMrsUuid);
    }

    [Fact]
    public void SinTasaDeRetornoEspontaneo_SoloVuelveQuienTieneCita()
    {
        var conCita = P("cita", Hoy);
        var sinCita = P("sin-cita");

        var sel = SoloCitas([conCita, sinCita], asistenciaProb: 1.0, new Random(8));

        Assert.Single(sel);
        Assert.Equal("cita", sel[0].OpenMrsUuid);
    }

    [Fact]
    public void SinElegibles_DevuelveVacio()
    {
        Assert.Empty(RecurrentSelector.Seleccionar(
            [], Hoy, 3, 1.0, 0.0, 1.0, Ventana, new Random(9)));
    }

    // ── Churn: el paciente descontento se aleja de la clínica ──────────────────────────────────────

    [Fact]
    public void Insatisfecho_NoVuelvePorSuCuenta()
    {
        var contento    = P("contento");
        var descontento = P("descontento", insatisfecho: true);

        var sel = RecurrentSelector.Seleccionar(
            [descontento, contento], Hoy, 3, 1.0, 0.0,
            probRetornoEspontaneo: 1.0, ventanaActividadDias: Ventana, new Random(10));

        Assert.Single(sel);
        Assert.Equal("contento", sel[0].OpenMrsUuid);
    }

    [Fact]
    public void InsatisfechoConCita_UsaSuPropiaProbabilidadDeAsistencia()
    {
        var descontento = P("descontento", Hoy, insatisfecho: true);

        // Su probabilidad es la de insatisfecho (0), no la general (1): no acude → su cita irá a Missed.
        Assert.Empty(SoloCitas([descontento], asistenciaProb: 1.0, new Random(11), asistenciaInsatisfecho: 0.0));

        // Con probabilidad 1 para insatisfechos sí acude: es una banda, no una exclusión.
        Assert.Single(SoloCitas([descontento], asistenciaProb: 0.0, new Random(12), asistenciaInsatisfecho: 1.0));
    }

    [Fact]
    public void SinCalificaciones_NadieEstaInsatisfecho()
    {
        // Red de seguridad: con la satisfacción apagada (ningún paciente marcado), nadie hace churn.
        var elegibles = new List<SimulatedPatient> { P("a", Hoy), P("b"), P("c") };

        var sel = RecurrentSelector.Seleccionar(
            elegibles, Hoy, 3, 1.0, 0.0, probRetornoEspontaneo: 1.0, ventanaActividadDias: Ventana,
            new Random(13));

        Assert.Equal(3, sel.Count);
    }

    // ── La tasa diaria sale del mando interpretable (visitas/paciente/año) ─────────────────────────

    [Fact]
    public void LaTasaDiariaSaleDeLasVisitasPorAno()
    {
        var re = new RecurrenceSettings { VisitasEspontaneasPorPacienteAno = 1.0 };

        var diaria = RecurrentSelector.ProbRetornoEspontaneoDiaria(re);

        // 1 visita/año ≈ 1 − e^(−1/365) ≈ 0,0027 diario.
        Assert.Equal(1 - Math.Exp(-1.0 / 365), diaria, precision: 8);
        // Y a lo largo de un año acumula ~63 % (1 − e^−1), que es lo que se espera de un Poisson.
        Assert.Equal(1 - Math.Exp(-1.0), 1 - Math.Pow(1 - diaria, 365), precision: 3);
    }

    [Fact]
    public void CeroVisitasEspontaneas_ApagaLaVia()
    {
        Assert.Equal(0, RecurrentSelector.ProbRetornoEspontaneoDiaria(
            new RecurrenceSettings { VisitasEspontaneasPorPacienteAno = 0 }));
    }

    // ── TieneCitaHoy (mismo criterio que usa el orquestador para el médico y el motivo) ────────────

    [Theory]
    [InlineData(0, true)]    // la cita es hoy
    [InlineData(-3, true)]   // llega 3 días tarde (límite de tolerancia)
    [InlineData(3, true)]    // se adelanta 3 días
    [InlineData(-4, false)]  // fuera de tolerancia: ya no es "su cita"
    [InlineData(4, false)]
    public void TieneCitaHoy_RespetaLaTolerancia(int offsetDias, bool esperado)
    {
        var p = P("x", Hoy.AddDays(offsetDias));
        Assert.Equal(esperado, RecurrentSelector.TieneCitaHoy(p, Hoy, toleranciaDias: 3));
    }

    [Fact]
    public void TieneCitaHoy_SinCita_EsFalso()
    {
        Assert.False(RecurrentSelector.TieneCitaHoy(P("x"), Hoy, toleranciaDias: 3));
    }
}
