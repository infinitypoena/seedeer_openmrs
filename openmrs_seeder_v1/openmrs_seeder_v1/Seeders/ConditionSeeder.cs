using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

/// <summary>
/// Agrega a la lista de problemas del paciente (POST /condition) los diagnósticos crónicos
/// (marcados cronica=true en el catálogo). Evita duplicados entre visitas recurrentes.
/// </summary>
public class ConditionSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly ILogger<ConditionSeeder> _logger;

    public ConditionSeeder(OpenMrsRestClient client, ILogger<ConditionSeeder> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var cronicos = patient.TodosDiagnosticos
            .Where(d => d.EsCronica && patient.ProblemListConcepts.Add(d.CielUuid))
            .ToList();

        foreach (var dx in cronicos)
        {
            var payload = new
            {
                patient        = patient.OpenMrsUuid,
                clinicalStatus = "ACTIVE",
                // coded debe ser un UUID en string plano (igual que codedAllergen), no un objeto
                condition      = new { coded = dx.CielUuid },
                onsetDate      = VisitSeeder.FormatDatetime(patient.VisitDatetime)
            };

            try
            {
                await _client.PostAsync("condition", payload, ct);
                _logger.LogInformation("[Condition] {Dx} agregado a lista de problemas de {Id}",
                    dx.NombreEs, patient.Identifier);
            }
            catch (Exception ex)
            {
                _logger.LogError("[Condition] Error agregando {Dx} para {Id}: {Msg}",
                    dx.NombreEs, patient.Identifier, ex.Message);
            }
        }
    }
}
