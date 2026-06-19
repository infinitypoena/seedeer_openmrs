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
    private readonly double _allergyProb;
    private readonly Random _rng = new();
    private readonly ILogger<AllergySeeder> _logger;

    public AllergySeeder(
        OpenMrsRestClient client,
        CatalogLoader catalogs,
        Configuration.SimulationSettings simSettings,
        ILogger<AllergySeeder> logger)
    {
        _client      = client;
        _catalogs    = catalogs;
        _allergyProb = simSettings.ReferralProbabilities.AllergyOnNew;
        _logger      = logger;
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
        if (_rng.NextDouble() >= _allergyProb)
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

        var cantidad = _rng.Next(1, Math.Min(4, pool.Count + 1));
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
                codedAllergen = new { uuid = alergeno.ConceptUuid }
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
