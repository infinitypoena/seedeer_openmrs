using Microsoft.Extensions.Configuration;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Sistema;

/// <summary>
/// El <c>appsettings.example.json</c> que se le da al usuario tiene que describir una clínica viable:
/// pasar el validador de arranque y proyectar una curva que no rompa las leyes juzgables antes de
/// sembrar (L5/L6). Si el ejemplo del repo está condenado, que lo diga la suite y no una corrida de horas.
/// </summary>
public class ConfiguracionRealTests
{
    [Fact]
    public void ElExampleJson_PasaElValidadorDeArranque()
    {
        var (sim, config) = Escenarios.DesdeExampleJson();
        var omrs = new OpenMrsSettings();
        config.GetSection("OpenMRS").Bind(omrs);

        var errores = SettingsValidator.Validate(sim, omrs);

        Assert.True(errores.Count == 0, string.Join("\n", errores));
    }

    [Fact]
    public void LaProyeccionDelExample_NoRompeL5NiL6()
    {
        var (sim, _) = Escenarios.DesdeExampleJson();
        var plan  = new DailyScheduleGenerator(sim).Generate();
        var curva = BassGrowthModel.Proyectar(plan, sim);

        var rotas = Invariantes.Rotas(Invariantes.EvaluarProyeccion(curva, sim));

        Assert.True(rotas.Count == 0,
            string.Join("\n", rotas.Select(l => $"{l.Codigo}: {l.Medido}")));
    }

    [Fact]
    public void ElAforoYElObjetivo_SonElMismoNumero()
    {
        // El contrato documentado: PacientesPorDiaObjetivo ES la capacidad de trabajo, por eso
        // CapacidadComodaPorDia vale lo mismo y el techo duro queda ≥1,3× por encima (es una red de
        // seguridad, no el aforo). SettingsValidator lo exige; el example debe cumplirlo.
        var (sim, _) = Escenarios.DesdeExampleJson();

        Assert.Equal(sim.Crecimiento.PacientesPorDiaObjetivo, sim.Satisfaccion.CapacidadComodaPorDia);
        Assert.True(sim.Crecimiento.PacientesPorDiaMax >= sim.Crecimiento.PacientesPorDiaObjetivo * 1.3);
    }
}
