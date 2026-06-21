using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class LabOrderSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly double _labOrderProb;
    private readonly double _urgentProb;
    private readonly Random _rng = new();
    private readonly ILogger<LabOrderSeeder> _logger;

    public LabOrderSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        SimulationSettings simSettings,
        ILogger<LabOrderSeeder> logger)
    {
        _client       = client;
        _settings     = settings;
        _catalogs     = catalogs;
        _labOrderProb = simSettings.ReferralProbabilities.LabOrder;
        _urgentProb   = simSettings.ReferralProbabilities.Urgent;
        _logger       = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patient.ConsultaEncounterUuid))
        {
            _logger.LogWarning("[LabOrder] Skip: sin encounter de consulta para {Id}", patient.Identifier);
            return;
        }

        var debeOrden = patient.TodosDiagnosticos.Any(d => d.RequiereLab)
            ? _rng.NextDouble() < 0.80
            : _rng.NextDouble() < _labOrderProb;

        if (!debeOrden) return;

        var candidatos = _catalogs.Laboratorios
            .Where(l => patient.Categorias.Any(c => AplicaCategoria(l, c)))
            .Where(l => !patient.OrderedConcepts.Contains(l.CielUuid)) // evita re-ordenar lo ya activo
            .ToList();

        if (candidatos.Count == 0) return;

        var cantidad = _rng.NextDouble() < 0.40 ? 2 : 1;
        var elegidos = candidatos.OrderBy(_ => _rng.Next()).Take(cantidad).ToList();

        int ordenesOk = 0;
        foreach (var lab in elegidos)
        {
            var esUrgente = patient.TodosDiagnosticos.Any(d => d.Severidad == "grave")
                ? _rng.NextDouble() < 0.50
                : _rng.NextDouble() < _urgentProb;

            var ok = await PostOrderAsync(patient, lab.CielUuid, esUrgente ? "STAT" : "ROUTINE", ct);
            if (ok) { ordenesOk++; patient.OrderedConcepts.Add(lab.CielUuid); }
        }

        _logger.LogInformation("[LabOrder] {N}/{Total} órdenes de lab para {Id}",
            ordenesOk, elegidos.Count, patient.Identifier);
    }

    private async Task<bool> PostOrderAsync(SimulatedPatient patient, string conceptUuid, string urgency, CancellationToken ct)
    {
        var payload = new
        {
            type        = "testorder",
            patient     = patient.OpenMrsUuid,
            concept     = conceptUuid,
            encounter   = patient.ConsultaEncounterUuid,
            orderer     = _settings.Defaults.ProviderUuid,
            careSetting = _settings.Defaults.OutpatientCareSettingUuid,
            urgency
        };

        try
        {
            await _client.PostAsync("order", payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LabOrder] Error en orden para {Id}: {Msg}", patient.Identifier, ex.Message);
            return false;
        }
    }

    private static bool AplicaCategoria(Models.Catalogs.LaboratorioEntry l, string cat) => cat switch
    {
        "respiratorio"   => l.AplicaRespiratorio,
        "cardiovascular" => l.AplicaCardiovascular,
        "diabetes"       => l.AplicaDiabetes,
        "digestivo"      => l.AplicaDigestivo,
        "osteomuscular"  => l.AplicaOsteomuscular,
        "urologico"      => l.AplicaUrologico,
        "infeccioso"     => l.AplicaInfeccioso,
        "endocrino"      => l.AplicaEndocrino,
        "neurologico"     => l.AplicaNeurologico,
        "dermatologico"   => l.AplicaDermatologico,
        "salud_mental"    => l.AplicaSaludMental,
        "ginecoobstetrico"=> l.AplicaGinecoobstetrico,
        "trauma"          => l.AplicaTrauma,
        _ => false
    };
}
