using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class PrescriptionSeeder
{
    private static readonly int[] Duraciones = [7, 14, 30];

    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly double _drugOrderProb;
    private readonly Random _rng = new();

    public PrescriptionSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        SimulationSettings simSettings)
    {
        _client       = client;
        _settings     = settings;
        _catalogs     = catalogs;
        _drugOrderProb = simSettings.ReferralProbabilities.DrugOrder;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patient.ConsultaEncounterUuid)) return;

        var debeRx = patient.Diagnostico?.RequiereRx == true
            ? _rng.NextDouble() < 0.90
            : _rng.NextDouble() < _drugOrderProb;

        if (!debeRx) return;

        var candidatos = _catalogs.Medicamentos
            .Where(m => AplicaCategoria(m, patient.Categoria))
            .ToList();

        if (candidatos.Count == 0) return;

        // 1-3 medicamentos por visita
        var cantidad = _rng.Next(1, Math.Min(4, candidatos.Count + 1));
        var elegidos = candidatos.OrderBy(_ => _rng.Next()).Take(cantidad).ToList();

        foreach (var med in elegidos)
            await PostDrugOrderAsync(patient, med, ct);
    }

    private async Task PostDrugOrderAsync(
        SimulatedPatient patient,
        Models.Catalogs.MedicamentoEntry med,
        CancellationToken ct)
    {
        var duracion = Duraciones[_rng.Next(Duraciones.Length)];

        var payload = new
        {
            type          = "drugorder",
            patient       = patient.OpenMrsUuid,
            drug          = med.DrugUuid,
            encounter     = patient.ConsultaEncounterUuid,
            orderer       = _settings.Defaults.ProviderUuid,
            careSetting   = _settings.Defaults.OutpatientCareSettingUuid,
            dose          = 1.0,
            doseUnits     = _settings.Defaults.TabletConceptUuid,
            route         = med.ViaUuid,
            frequency     = _settings.Defaults.OnceDailyFrequencyUuid,
            numRefills    = 0,
            duration      = duracion,
            durationUnits = _settings.Defaults.DaysConceptUuid
        };

        try { await _client.PostAsync("order", payload, ct); }
        catch (Exception ex) { Console.Error.WriteLine($"[PrescriptionSeeder] {patient.Identifier}: {ex.Message}"); }
    }

    private static bool AplicaCategoria(Models.Catalogs.MedicamentoEntry m, string cat) => cat switch
    {
        "respiratorio"   => m.AplicaRespiratorio,
        "cardiovascular" => m.AplicaCardiovascular,
        "diabetes"       => m.AplicaDiabetes,
        "digestivo"      => m.AplicaDigestivo,
        "osteomuscular"  => m.AplicaOsteomuscular,
        "urologico"      => m.AplicaUrologico,
        "infeccioso"     => m.AplicaInfeccioso,
        "endocrino"      => m.AplicaEndocrino,
        _ => false
    };
}
