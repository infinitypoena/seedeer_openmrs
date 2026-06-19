using System.Text.Json;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

public class PatientSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;

    public PatientSeeder(OpenMrsRestClient client, OpenMrsSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<string?> CreateAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var payload = new
        {
            person = new
            {
                names = new[]
                {
                    new { givenName = patient.GivenName, familyName = patient.FamilyName, preferred = true }
                },
                gender    = patient.Gender,
                birthdate = patient.BirthDate.ToString("yyyy-MM-dd"),
                addresses = new[]
                {
                    new
                    {
                        address1    = patient.Address1,
                        cityVillage = patient.City,
                        country     = "España",
                        preferred   = true
                    }
                }
            },
            identifiers = new[]
            {
                new
                {
                    identifier     = patient.Identifier,
                    identifierType = _settings.Defaults.PatientIdentifierTypeUuid,
                    location       = _settings.Defaults.LocationUuid,
                    preferred      = true
                }
            }
        };

        try
        {
            var json = await _client.PostAsync("patient", payload, ct);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("uuid", out var uuidProp))
                return uuidProp.GetString();
        }
        catch (Exception ex)
        {
            // Log failure but don't crash the pipeline
            Console.Error.WriteLine($"[PatientSeeder] Error creando {patient.Identifier}: {ex.Message}");
        }

        return null;
    }
}
