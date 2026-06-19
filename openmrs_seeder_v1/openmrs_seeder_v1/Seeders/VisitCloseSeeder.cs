using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

public class VisitCloseSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly Random _rng = new();
    private readonly ILogger<VisitCloseSeeder> _logger;

    public VisitCloseSeeder(OpenMrsRestClient client, ILogger<VisitCloseSeeder> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Cierra la visita estableciendo stopDatetime entre 1 y 4 horas después de la llegada.
    /// </summary>
    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patient.VisitUuid))
        {
            _logger.LogWarning("[VisitClose] Skip: sin VisitUuid para {Id}", patient.Identifier);
            return;
        }

        var duracionMinutos = _rng.Next(60, 241);
        var stopDatetime    = patient.VisitDatetime.AddMinutes(duracionMinutos);

        var payload = new { stopDatetime = VisitSeeder.FormatDatetime(stopDatetime) };

        try
        {
            await _client.PostAsync($"visit/{patient.VisitUuid}", payload, ct);
            _logger.LogInformation("[VisitClose] Visita {VisitUuid} cerrada para {Id}",
                patient.VisitUuid, patient.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError("[VisitClose] Error cerrando visita de {Id}: {Msg}", patient.Identifier, ex.Message);
        }
    }
}
