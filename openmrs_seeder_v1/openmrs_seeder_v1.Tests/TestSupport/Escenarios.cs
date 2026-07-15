using Microsoft.Extensions.Configuration;
using OpenmrsSeeder.Configuration;

namespace openmrs_seeder_v1.Tests.TestSupport;

/// <summary>
/// Constructores de <see cref="SimulationSettings"/> para los escenarios que la suite monta una y otra
/// vez. Centralizados aquí: si la configuración gana un campo, se toca un solo sitio.
/// </summary>
public static class Escenarios
{
    /// <summary>Configuración base determinista (semilla fija), con la ventana pedida.</summary>
    public static SimulationSettings Sim(DateTime? inicio = null, DateTime? fin = null, int seed = 42)
    {
        var start = inicio ?? new DateTime(2023, 1, 1);
        return new SimulationSettings
        {
            StartDate  = start,
            EndDate    = fin ?? start.AddYears(3),
            RandomSeed = seed,
        };
    }

    /// <summary>
    /// El escenario de crecimiento canónico: la clínica <b>arranca</b> en <paramref name="arranque"/>
    /// altas/día y debe <b>aterrizar</b> en <paramref name="objetivo"/> visitas/día. Los defaults son los
    /// documentados en CLAUDE.md (6 → 25 en 3 años, área de 60.000).
    /// </summary>
    public static SimulationSettings SimObjetivo(
        int arranque = 6,
        int objetivo = 25,
        int anios = 3,
        int poblacion = 60000,
        int ventanaDias = 365,
        int max = 45,
        int seed = 42)
    {
        var sim = Sim(fin: new DateTime(2023, 1, 1).AddYears(anios), seed: seed);
        sim.PacientesPorDiaMedio                = arranque;
        sim.Crecimiento.Enabled                 = true;
        sim.Crecimiento.PacientesPorDiaObjetivo = objetivo;
        sim.Crecimiento.PacientesPorDiaMax      = max;
        sim.Crecimiento.PoblacionCaptacion      = poblacion;
        sim.Crecimiento.VentanaActividadDias    = ventanaDias;
        // El aforo cómodo ES el objetivo (lo exige SettingsValidator y lo asume CapacidadDelDia).
        sim.Satisfaccion.CapacidadComodaPorDia  = objetivo;
        return sim;
    }

    /// <summary>
    /// El <c>appsettings.example.json</c> REAL del repo, ligado a <see cref="SimulationSettings"/> con el
    /// mismo binder que usa <c>Program.cs</c>. Si el ejemplo que se le da al usuario está roto, que lo
    /// diga la suite y no su primera corrida.
    /// </summary>
    public static (SimulationSettings Sim, IConfigurationRoot Config) DesdeExampleJson()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Repo.AppSettingsExample(), optional: false)
            .Build();
        var sim = new SimulationSettings();
        config.GetSection("Simulation").Bind(sim);
        return (sim, config);
    }
}
