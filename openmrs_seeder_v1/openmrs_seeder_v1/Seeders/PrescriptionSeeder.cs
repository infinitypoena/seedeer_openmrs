using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PrescriptionSeeder> _logger;

    public PrescriptionSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        SimulationSettings simSettings,
        ILogger<PrescriptionSeeder> logger)
    {
        _client        = client;
        _settings      = settings;
        _catalogs      = catalogs;
        _drugOrderProb = simSettings.ReferralProbabilities.DrugOrder;
        _logger        = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patient.ConsultaEncounterUuid))
        {
            _logger.LogWarning("[Prescription] Skip: sin encounter de consulta para {Id}", patient.Identifier);
            return;
        }

        var debeRx = patient.TodosDiagnosticos.Any(d => d.RequiereRx)
            ? _rng.NextDouble() < 0.90
            : _rng.NextDouble() < _drugOrderProb;

        if (!debeRx) return;

        var candidatos = _catalogs.Medicamentos
            .Where(m => patient.Categorias.Any(c => AplicaCategoria(m, c)))
            .Where(m => !patient.OrderedConcepts.Contains(m.ConceptUuid)) // evita re-ordenar lo ya activo
            .ToList();

        if (candidatos.Count == 0) return;

        var cantidad = _rng.Next(1, Math.Min(4, candidatos.Count + 1));
        var elegidos = candidatos.OrderBy(_ => _rng.Next()).Take(cantidad).ToList();

        int rxOk = 0;
        foreach (var med in elegidos)
        {
            var ok = await PostDrugOrderAsync(patient, med, ct);
            if (ok) { rxOk++; patient.OrderedConcepts.Add(med.ConceptUuid); }
        }
        _logger.LogInformation("[Prescription] {N}/{Total} prescripciones para {Id}",
            rxOk, elegidos.Count, patient.Identifier);
    }

    private async Task<bool> PostDrugOrderAsync(
        SimulatedPatient patient,
        Models.Catalogs.MedicamentoEntry med,
        CancellationToken ct)
    {
        var duracion = Duraciones[_rng.Next(Duraciones.Length)];

        var payload = new
        {
            type          = "drugorder",
            patient       = patient.OpenMrsUuid,
            concept       = med.ConceptUuid,
            drug          = med.DrugUuid,
            encounter     = patient.ConsultaEncounterUuid,
            orderer       = _settings.Defaults.ProviderUuid,
            careSetting   = _settings.Defaults.OutpatientCareSettingUuid,
            dose          = 1.0,
            doseUnits     = _settings.Defaults.TabletConceptUuid,
            route         = med.ViaUuid,
            frequency     = _settings.Defaults.OnceDailyFrequencyUuid,
            numRefills    = 0,
            quantity      = (double)duracion,
            quantityUnits = _settings.Defaults.TabletConceptUuid,
            duration      = duracion,
            durationUnits = _settings.Defaults.DaysConceptUuid
        };

        try
        {
            await _client.PostAsync("order", payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Prescription] Error en prescripción para {Id}: {Msg}", patient.Identifier, ex.Message);
            return false;
        }
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
        "neurologico"     => m.AplicaNeurologico,
        "dermatologico"   => m.AplicaDermatologico,
        "salud_mental"    => m.AplicaSaludMental,
        "ginecoobstetrico"=> m.AplicaGinecoobstetrico,
        "trauma"          => m.AplicaTrauma,
        _ => false
    };
}
