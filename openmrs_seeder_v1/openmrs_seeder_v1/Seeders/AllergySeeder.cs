using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class AllergySeeder
{
    // CIEL severity concepts estándar OpenMRS
    private const string SeverityMildUuid     = "1498AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SeverityModerateUuid = "1499AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SeveritySevereUuid   = "1500AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    // Reacción genérica: Rash (aplicable a todos los tipos de alérgeno)
    private const string ReactionRashUuid = "512AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly OpenMrsRestClient _client;
    private readonly CatalogLoader _catalogs;
    private readonly double _allergyProb;
    private readonly Random _rng = new();

    public AllergySeeder(
        OpenMrsRestClient client,
        CatalogLoader catalogs,
        Configuration.SimulationSettings simSettings)
    {
        _client      = client;
        _catalogs    = catalogs;
        _allergyProb = simSettings.ReferralProbabilities.AllergyOnNew;
    }

    /// <summary>
    /// Registra alergias para un paciente nuevo si la probabilidad aplica.
    /// Llamar DESPUÉS de que el paciente tenga OpenMrsUuid asignado.
    /// </summary>
    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (!patient.EsNuevo) return;
        if (_rng.NextDouble() >= _allergyProb) return;
        if (string.IsNullOrEmpty(patient.OpenMrsUuid)) return;

        var pool = _catalogs.Alergenos.ToList();
        if (pool.Count == 0) return;

        var cantidad = _rng.Next(1, Math.Min(4, pool.Count + 1));
        var elegidos = pool.OrderBy(_ => _rng.Next()).Take(cantidad).ToList();

        foreach (var alergeno in elegidos)
            await PostAllergyAsync(patient.OpenMrsUuid, alergeno, ct);
    }

    private async Task PostAllergyAsync(
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
                allergenType   = alergeno.TipoAlergeno,
                codedAllergen  = new { uuid = alergeno.ConceptUuid }
            },
            severity  = new { uuid = severityUuid },
            reactions = new[] { new { reaction = new { uuid = ReactionRashUuid } } }
        };

        try
        {
            await _client.PostAsync($"patient/{patientUuid}/allergy", payload, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AllergySeeder] {patientUuid}: {ex.Message}");
        }
    }
}
