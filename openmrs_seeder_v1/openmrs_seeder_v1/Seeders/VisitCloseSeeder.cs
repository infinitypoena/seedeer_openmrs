using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

public class VisitCloseSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly Random _rng;
    private readonly ILogger<VisitCloseSeeder> _logger;

    public VisitCloseSeeder(OpenMrsRestClient client, Configuration.SimulationSettings simSettings, ILogger<VisitCloseSeeder> logger)
    {
        _client = client;
        _rng    = new Random(simSettings.RandomSeed + 16);
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
        var stopDatetime    = HoraCierre(
            patient.VisitDatetime, duracionMinutos, patient.UltimoEncuentroDatetime);

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

    /// <summary>
    /// Hora de cierre de la visita: la llegada + su duración, pero <b>nunca antes del último encuentro</b>
    /// (la toma de muestra en el laboratorio ocurre después de la consulta y puede caer más tarde que la
    /// duración sorteada). OpenMRS rechaza cerrar una visita dejando fuera a uno de sus encuentros, así que
    /// en ese caso se alarga hasta 15 min después de él. Seam puro (testeable sin red).
    /// </summary>
    public static DateTime HoraCierre(DateTime llegada, int duracionMinutos, DateTime? ultimoEncuentro)
    {
        var cierre = llegada.AddMinutes(duracionMinutos);
        if (ultimoEncuentro is { } ultimo && ultimo >= cierre)
            cierre = ultimo.AddMinutes(15);
        return cierre;
    }
}
