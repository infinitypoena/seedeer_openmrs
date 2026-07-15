using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Sistema;

/// <summary>
/// L3 es un <b>tripwire estructural</b>: el orquestador de producción pasa <c>retornosDesplazados: 0</c>
/// literal (SeedOrchestrator.cs, cierre del día), así que en una corrida real la ley no puede saltar por
/// construcción. Existe para el día en que alguien reintroduzca un cupo y ese literal deje de ser cero
/// — por eso se testea AQUÍ, a nivel de <see cref="Invariantes"/> con el contador inyectado, y en
/// <see cref="SistemaRotoTests"/> con el cupo de verdad corriendo en la MiniClinica.
/// </summary>
public class L3EstructuralTests
{
    [Fact]
    public void UnSoloRetornoDesplazadoPorElAforo_EnciendeElTripwire()
    {
        var sim = Escenarios.SimObjetivo();

        var sano = Estadisticas.Sembrar([(2023, 100, 100)]);
        var roto = Estadisticas.Sembrar([(2023, 100, 100)], retornosDesplazados: 1);

        Assert.True(Invariantes.Evaluar(sano, [], sim).Single(l => l.Codigo == "L3").Cumple);
        Assert.False(Invariantes.Evaluar(roto, [], sim).Single(l => l.Codigo == "L3").Cumple);
    }

    [Fact]
    public void L3AplicaSiempre_HastaEnLaCorridaMasCorta()
    {
        // A diferencia de L1/L7, el tripwire no tiene ventana mínima: un retorno desplazado es un bug
        // el día 1 igual que el día 1000.
        var sim = Escenarios.SimObjetivo();
        sim.EndDate = sim.StartDate.AddDays(7);

        var ley = Invariantes.Evaluar(Estadisticas.Sembrar([(2023, 10, 0)]), [], sim)
            .Single(l => l.Codigo == "L3");

        Assert.True(ley.Aplica);
    }

    [Fact]
    public void LasAltasRechazadasPorAforo_NoSonElTripwire()
    {
        // El freno sano (la consulta llena deja de CAPTAR) no debe confundirse con el bug (dejar sin
        // atender a quien tenía cita): mil altas rechazadas no encienden L3.
        var sim   = Escenarios.SimObjetivo();
        var stats = Estadisticas.Sembrar([(2023, 100, 100)]);
        stats.RegistrarDiaSimulado(atendidos: 25, nuevosRechazados: 1000, retornosDesplazados: 0, topoElTecho: false);

        Assert.True(Invariantes.Evaluar(stats, [], sim).Single(l => l.Codigo == "L3").Cumple);
    }
}
