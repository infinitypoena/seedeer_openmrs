using System.Text.Json;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

public class VisitSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;

    public VisitSeeder(OpenMrsRestClient client, OpenMrsSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<string?> CreateAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var payload = new
        {
            patient      = patient.OpenMrsUuid,
            visitType    = _settings.Defaults.VisitTypeUuid,
            startDatetime = FormatDatetime(patient.VisitDatetime),
            location     = _settings.Defaults.LocationUuid,
            description  = "SEEDED_BY_SIMULATOR"
        };

        try
        {
            var json = await _client.PostAsync("visit", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("uuid", out var uuid)) return uuid.GetString();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VisitSeeder] {patient.Identifier}: {ex.Message}");
        }
        return null;
    }

    internal static string FormatDatetime(DateTime dt) =>
        dt.ToString("yyyy-MM-dd'T'HH:mm:ss.000+0000");
}
