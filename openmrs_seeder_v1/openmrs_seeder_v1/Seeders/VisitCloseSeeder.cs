using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

public class VisitCloseSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly Random _rng = new();

    public VisitCloseSeeder(OpenMrsRestClient client) => _client = client;

    /// <summary>
    /// Cierra la visita estableciendo stopDatetime entre 1 y 4 horas después de la llegada.
    /// </summary>
    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patient.VisitUuid)) return;

        var duracionMinutos = _rng.Next(60, 241); // 1h–4h
        var stopDatetime    = patient.VisitDatetime.AddMinutes(duracionMinutos);

        var payload = new
        {
            stopDatetime = VisitSeeder.FormatDatetime(stopDatetime)
        };

        try
        {
            await _client.PostAsync($"visit/{patient.VisitUuid}", payload, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VisitCloseSeeder] {patient.Identifier}: {ex.Message}");
        }
    }
}
