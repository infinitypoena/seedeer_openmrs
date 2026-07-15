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
        // 60 días: por debajo del umbral de L1 (90 d), de L7 (365 d), de L8 (VentanaActividadDias) y con
        // un solo año natural (L4 necesita dos).
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
    public void CientoVeinteDias_L1YaSeJuzga_PeroL7yL8TodaviaNo()
    {
        // El borde: a los 120 días la ley del crónico ya procede (umbral 90 d) y las anuales siguen calladas.
        var sim = Escenarios.SimObjetivo();
        sim.EndDate = sim.StartDate.AddDays(120);

        var r = MiniClinica.Correr(sim);

        Assert.True(r.Ley("L1").Aplica);
        Assert.False(r.Ley("L7").Aplica);
        Assert.False(r.Ley("L8").Aplica);
    }
}
