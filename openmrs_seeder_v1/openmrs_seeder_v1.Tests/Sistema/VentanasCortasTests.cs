using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Sistema;

/// <summary>
/// Una corrida corta <b>no puede juzgar</b> la continuidad longitudinal: los controles crónicos caen a
/// 30-120 días y la maduración del panel se mide en años. Las leyes que no dan tiempo a pronunciarse
/// tienen que decir "no procede" (<c>Aplica=false</c>) — ni aprobar en falso ni suspender en falso.
/// Es el modo validación documentado (1 semana / 2 meses) y no debe salir en rojo.
/// </summary>
public class VentanasCortasTests
{
    [Fact]
    public void DosMesesDeCorrida_LasLeyesLentasNoProceden_YNadaSaleEnRojo()
    {
        // 60 días: por debajo del umbral de continuidad (365 d) — L1, L7, L8 calladas; L4 necesita dos años.
        var sim = Escenarios.SimObjetivo();
        sim.EndDate = sim.StartDate.AddDays(60);

        var r = MiniClinica.Correr(sim);

        Assert.False(r.Ley("L1").Aplica);
        Assert.False(r.Ley("L4").Aplica);
        Assert.False(r.Ley("L7").Aplica);
        Assert.False(r.Ley("L8").Aplica);
        Assert.Empty(r.Rotas);
    }

    [Fact]
    public void CientoVeinteDias_LasLeyesDeContinuidadSiguenCalladas()
    {
        // 120 días: aún por debajo del año. L1 no se juzga porque el control crónico se cita a 30-120 días
        // — a esta altura la mayoría de los crónicos ni ha tenido su cita. Antes L1 se juzgaba a los 90 d
        // y falseaba: toda corrida de 3 meses rompía la ley sin estar rota.
        var sim = Escenarios.SimObjetivo();
        sim.EndDate = sim.StartDate.AddDays(120);

        var r = MiniClinica.Correr(sim);

        Assert.False(r.Ley("L1").Aplica);
        Assert.False(r.Ley("L7").Aplica);
        Assert.False(r.Ley("L8").Aplica);
        Assert.Empty(r.Rotas);
    }

    [Fact]
    public void UnAno_L1SeJuzga_YElCronicoVuelve()
    {
        // Un año: L1 cruza el umbral de continuidad y, con la configuración sana, da verde (medido en la
        // MiniClinica: ~65 % de los crónicos vuelve a los 365 días). L4 sigue callada (dos años naturales).
        var sim = Escenarios.SimObjetivo();
        sim.EndDate = sim.StartDate.AddDays(365);

        var r = MiniClinica.Correr(sim);

        Assert.True(r.Ley("L1").Aplica);
        Assert.True(r.Ley("L1").Cumple, r.Ley("L1").Medido);
    }
}
