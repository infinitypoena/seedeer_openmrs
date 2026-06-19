using System.Text.Json;
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

    public LabOrderSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        SimulationSettings simSettings)
    {
        _client       = client;
        _settings     = settings;
        _catalogs     = catalogs;
        _labOrderProb = simSettings.ReferralProbabilities.LabOrder;
        _urgentProb   = simSettings.ReferralProbabilities.Urgent;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patient.ConsultaEncounterUuid)) return;

        var debeOrden = patient.Diagnostico?.RequiereLab == true
            ? _rng.NextDouble() < 0.80
            : _rng.NextDouble() < _labOrderProb;

        if (!debeOrden) return;

        var candidatos = _catalogs.Laboratorios
            .Where(l => AplicaCategoria(l, patient.Categoria))
            .ToList();

        if (candidatos.Count == 0) return;

        // 1 o 2 órdenes por visita
        var cantidad = _rng.NextDouble() < 0.40 ? 2 : 1;
        var elegidos = candidatos.OrderBy(_ => _rng.Next()).Take(cantidad).ToList();

        foreach (var lab in elegidos)
        {
            var esUrgente = patient.Diagnostico?.Severidad == "grave"
                ? _rng.NextDouble() < 0.50
                : _rng.NextDouble() < _urgentProb;

            await PostOrderAsync(patient, lab.CielUuid, esUrgente ? "URGENT" : "ROUTINE", ct);
        }
    }

    private async Task PostOrderAsync(SimulatedPatient patient, string conceptUuid, string urgency, CancellationToken ct)
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

        try { await _client.PostAsync("order", payload, ct); }
        catch (Exception ex) { Console.Error.WriteLine($"[LabOrderSeeder] {patient.Identifier}: {ex.Message}"); }
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
        _ => false
    };
}
