using Microsoft.Extensions.Configuration;
using OpenmrsSeeder.Configuration;

namespace openmrs_seeder_v1.Tests.Infraestructura;

public class SettingsValidatorTests
{
    private static SimulationSettings SimValida() => new();

    private static OpenMrsSettings OmrsValido() => new()
    {
        RestApi = new RestApiSettings { BaseUrl = "http://localhost/openmrs/ws/rest/v1" }
    };

    [Fact]
    public void Validate_ConfigPorDefecto_SinViolaciones()
    {
        var violaciones = SettingsValidator.Validate(SimValida(), OmrsValido());

        Assert.Empty(violaciones);
    }

    [Fact]
    public void Validate_ProbabilidadFueraDeRango_ReportaElCampo()
    {
        var sim = SimValida();
        sim.ReferralProbabilities.FollowUp = 1.5;

        var violaciones = SettingsValidator.Validate(sim, OmrsValido());

        var v = Assert.Single(violaciones);
        Assert.Contains("ReferralProbabilities.FollowUp", v);
    }

    [Fact]
    public void Validate_BandaInvertida_ReportaViolacion()
    {
        var sim = SimValida();
        sim.CommonProbMin = 0.9;
        sim.CommonProbMax = 0.8;

        var violaciones = SettingsValidator.Validate(sim, OmrsValido());

        var v = Assert.Single(violaciones);
        Assert.Contains("CommonProbMin", v);
    }

    [Fact]
    public void Validate_ChequeoProbFueraDeRango_ReportaElCampo()
    {
        var sim = SimValida();
        sim.Chequeo.ProbabilidadAlta = 1.2;

        var violaciones = SettingsValidator.Validate(sim, OmrsValido());

        var v = Assert.Single(violaciones);
        Assert.Contains("Chequeo.ProbabilidadAlta", v);
    }

    [Fact]
    public void Validate_ChequeoMinLabsInvalido_ReportaViolacion()
    {
        var sim = SimValida();
        sim.Chequeo.MinLabs = 0;

        var violaciones = SettingsValidator.Validate(sim, OmrsValido());

        var v = Assert.Single(violaciones);
        Assert.Contains("Chequeo.MinLabs", v);
    }

    [Fact]
    public void Validate_ChequeoMinLabsMayorQueMax_ReportaViolacion()
    {
        var sim = SimValida();
        sim.Chequeo.MinLabs = 5;
        sim.Chequeo.MaxLabs = 3;

        var violaciones = SettingsValidator.Validate(sim, OmrsValido());

        var v = Assert.Single(violaciones);
        Assert.Contains("Chequeo.MinLabs", v);
    }

    [Fact]
    public void Validate_StartDatePosteriorAEndDate_ReportaViolacion()
    {
        var sim = SimValida();
        sim.StartDate = new DateTime(2025, 6, 1);
        sim.EndDate = new DateTime(2025, 1, 1);

        var violaciones = SettingsValidator.Validate(sim, OmrsValido());

        var v = Assert.Single(violaciones);
        Assert.Contains("StartDate", v);
    }

    [Fact]
    public void Validate_CorregirFechasSinCadenaDeConexion_ReportaViolacion()
    {
        // Activar la corrección sin conexión dejaría la etapa 5/5 muerta en silencio
        var omrs = OmrsValido();
        omrs.Database.CorregirFechas = true;

        var violaciones = SettingsValidator.Validate(SimValida(), omrs);

        var v = Assert.Single(violaciones);
        Assert.Contains("ConnectionString", v);
    }

    [Fact]
    public void Validate_PrefijoPacienteVacio_ReportaViolacion()
    {
        // El prefijo es lo que acota qué filas puede tocar el proceso: vacío = alcance ilimitado
        var omrs = OmrsValido();
        omrs.Database.PrefijoPaciente = "";

        var violaciones = SettingsValidator.Validate(SimValida(), omrs);

        var v = Assert.Single(violaciones);
        Assert.Contains("PrefijoPaciente", v);
    }

    [Fact]
    public void Validate_CorregirFechasDesactivado_NoExigeCadenaDeConexion()
    {
        var omrs = OmrsValido();   // CorregirFechas = false por defecto, ConnectionString vacío

        Assert.Empty(SettingsValidator.Validate(SimValida(), omrs));
    }

    [Fact]
    public void Validate_VariasViolaciones_ReportaTodasSinCortocircuito()
    {
        var sim = SimValida();
        sim.SeguimientoCronicoProb = -0.1;
        sim.PacientesPorDiaMedio = 0;
        sim.Recurrence.MinDiasAgudo = 30;
        sim.Recurrence.MaxDiasAgudo = 7;
        var omrs = new OpenMrsSettings(); // BaseUrl vacío

        var violaciones = SettingsValidator.Validate(sim, omrs);

        Assert.Equal(4, violaciones.Count);
        Assert.Contains(violaciones, v => v.Contains("SeguimientoCronicoProb"));
        Assert.Contains(violaciones, v => v.Contains("PacientesPorDiaMedio"));
        Assert.Contains(violaciones, v => v.Contains("Recurrence.MinDiasAgudo"));
        Assert.Contains(violaciones, v => v.Contains("BaseUrl"));
    }

    [Fact]
    public void Validate_MinMedicosMayorQueMax_ReportaViolacion()
    {
        var sim = SimValida();
        sim.MinMedicosPorDia = 4;
        sim.MaxMedicosPorDia = 2;

        var violaciones = SettingsValidator.Validate(sim, OmrsValido());

        var v = Assert.Single(violaciones);
        Assert.Contains("MinMedicosPorDia", v);
    }

    private static IConfigurationSection SeccionSimulation(Dictionary<string, string?> valores)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        return config.GetSection("Simulation");
    }

    [Fact]
    public void FindUnknownKeys_ClaveHuerfanaAnidada_LaDetecta()
    {
        // El caso real que motivó la feature: AllergyOnNew quedó huérfana al moverse a la sección Allergy.
        var section = SeccionSimulation(new()
        {
            ["Simulation:PacientesPorDiaMedio"] = "15",
            ["Simulation:ReferralProbabilities:LabOrder"] = "0.4",
            ["Simulation:ReferralProbabilities:AllergyOnNew"] = "0.15"
        });

        var desconocidas = SettingsValidator.FindUnknownKeys(section, typeof(SimulationSettings));

        var clave = Assert.Single(desconocidas);
        Assert.Equal("Simulation:ReferralProbabilities:AllergyOnNew", clave);
    }

    [Fact]
    public void FindUnknownKeys_ClaveDeNivelSuperior_LaDetecta()
    {
        var section = SeccionSimulation(new()
        {
            ["Simulation:ClinicType"] = "ConsultaExterna",
            ["Simulation:RandomSeed"] = "42"
        });

        var desconocidas = SettingsValidator.FindUnknownKeys(section, typeof(SimulationSettings));

        var clave = Assert.Single(desconocidas);
        Assert.Equal("Simulation:ClinicType", clave);
    }

    [Fact]
    public void FindUnknownKeys_ClavesLibresDeDiccionariosYListas_NoLasReporta()
    {
        var section = SeccionSimulation(new()
        {
            ["Simulation:Comorbidity:AgeScaling:0-14"] = "0.3",
            ["Simulation:Comorbidity:AgeScaling:65+"] = "1.8",
            ["Simulation:DemographicProfile:AgeGroups:0:Label"] = "0-14",
            ["Simulation:DemographicProfile:AgeGroups:0:Weight"] = "20"
        });

        var desconocidas = SettingsValidator.FindUnknownKeys(section, typeof(SimulationSettings));

        Assert.Empty(desconocidas);
    }

    [Fact]
    public void FindUnknownKeys_ConfigCorrecta_NoReportaNada()
    {
        var section = SeccionSimulation(new()
        {
            ["Simulation:StartDate"] = "2024-01-01",
            ["Simulation:Allergy:BaseProbabilityMin"] = "0.15",
            ["Simulation:Appointments:ToleranciaDias"] = "3"
        });

        var desconocidas = SettingsValidator.FindUnknownKeys(section, typeof(SimulationSettings));

        Assert.Empty(desconocidas);
    }
}
