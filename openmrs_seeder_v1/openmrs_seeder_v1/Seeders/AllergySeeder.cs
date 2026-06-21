using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class AllergySeeder
{
    private const string SeverityMildUuid     = "1498AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SeverityModerateUuid = "1499AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SeveritySevereUuid   = "1500AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ReactionRashUuid     = "512AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly OpenMrsRestClient _client;
    private readonly CatalogLoader _catalogs;
    private readonly Configuration.AllergySettings _settings;
    private readonly double _runAllergyProb;
    private readonly Random _rng = new();
    private readonly ILogger<AllergySeeder> _logger;

    public AllergySeeder(
        OpenMrsRestClient client,
        CatalogLoader catalogs,
        Configuration.SimulationSettings simSettings,
        ILogger<AllergySeeder> logger)
    {
        _client   = client;
        _catalogs = catalogs;
        _settings = simSettings.Allergy;
        _logger   = logger;

        // Prevalencia de la corrida: una vez por instancia (AllergySeeder es Transient → una por corrida),
        // sorteada uniformemente en [Min, Max] igual que la P(común) del orquestador.
        var min = _settings.BaseProbabilityMin;
        var max = Math.Max(min, _settings.BaseProbabilityMax);
        _runAllergyProb = min + _rng.NextDouble() * (max - min);
        _logger.LogInformation("[Allergy] Prevalencia de la corrida: {P:P0}", _runAllergyProb);
    }

    /// <summary>
    /// Registra alergias para un paciente nuevo si la probabilidad aplica.
    /// Llamar DESPUÉS de que el paciente tenga OpenMrsUuid asignado.
    /// </summary>
    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (!patient.EsNuevo)
        {
            _logger.LogDebug("[Allergy] Skip: paciente recurrente ({Id})", patient.Identifier);
            return;
        }
        if (_rng.NextDouble() >= _runAllergyProb)
        {
            _logger.LogDebug("[Allergy] Skip: probabilidad no alcanzada ({Id})", patient.Identifier);
            return;
        }
        if (string.IsNullOrEmpty(patient.OpenMrsUuid)) return;

        var pool = _catalogs.Alergenos.ToList();
        if (pool.Count == 0)
        {
            _logger.LogWarning("[Allergy] Catálogo de alergenos vacío, no se siembran alergias");
            return;
        }

        var cantidad = DecidirCantidad(pool.Count);
        var elegidos = pool.OrderBy(_ => _rng.Next()).Take(cantidad).ToList();

        int sembradas = 0;
        foreach (var alergeno in elegidos)
        {
            var ok = await PostAllergyAsync(patient.Identifier, patient.OpenMrsUuid, alergeno, ct);
            if (ok) sembradas++;
        }

        _logger.LogInformation("[Allergy] {N}/{Total} alergias sembradas para {Id}",
            sembradas, elegidos.Count, patient.Identifier);
    }

    private int DecidirCantidad(int poolCount) =>
        DecidirCantidad(poolCount, _settings, _rng.NextDouble);

    /// <summary>
    /// Número de alergias para un paciente que ya resultó alérgico, con decaída condicional:
    /// todos tienen ≥1; suma una 2ª con SecondAllergyProbability; suma una 3ª (dado que tiene 2)
    /// con ThirdAllergyProbability. Acotado por MaxAllergies y por el tamaño del catálogo.
    /// Método puro (RNG inyectado) para ser testeable sin red.
    /// </summary>
    public static int DecidirCantidad(
        int poolCount, Configuration.AllergySettings settings, Func<double> nextDouble)
    {
        var tope = Math.Min(settings.MaxAllergies, poolCount);
        if (tope <= 1) return Math.Max(0, tope);

        var cantidad = 1;
        if (nextDouble() < settings.SecondAllergyProbability)
        {
            cantidad = 2;
            if (tope >= 3 && nextDouble() < settings.ThirdAllergyProbability)
                cantidad = 3;
        }
        return Math.Min(cantidad, tope);
    }

    private async Task<bool> PostAllergyAsync(
        string identifier,
        string patientUuid,
        Models.Catalogs.AlergenoEntry alergeno,
        CancellationToken ct)
    {
        var severityUuid = alergeno.SeveridadTipica switch
        {
            "grave"    => SeveritySevereUuid,
            "moderada" => SeverityModerateUuid,
            _          => SeverityMildUuid
        };

        var payload = new
        {
            allergen = new
            {
                allergenType  = alergeno.TipoAlergeno,
                codedAllergen = alergeno.ConceptUuid
            },
            severity  = new { uuid = severityUuid },
            reactions = new[] { new { reaction = new { uuid = ReactionRashUuid } } }
        };

        try
        {
            await _client.PostAsync($"patient/{patientUuid}/allergy", payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Allergy] Error en alérgeno '{Alergeno}' para {Id}: {Msg}",
                alergeno.NombreEs, identifier, ex.Message);
            return false;
        }
    }
}
