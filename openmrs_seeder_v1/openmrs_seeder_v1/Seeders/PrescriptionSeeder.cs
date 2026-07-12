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
    private readonly Random _rng;
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
        _rng = new Random(simSettings.RandomSeed + 15);
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

        var fechaVisita = DateOnly.FromDateTime(patient.VisitDatetime);
        var candidatos = _catalogs.Medicamentos
            .Where(m => patient.Categorias.Any(c => AplicaCategoria(m, c)))
            .Where(m => !OrderVigencia.EstaActivo(patient.OrderedConcepts, m.ConceptUuid, fechaVisita)) // solo si no hay una receta aún vigente
            .ToList();

        if (candidatos.Count == 0) return;

        var cantidad = _rng.Next(1, Math.Min(4, candidatos.Count + 1));
        var elegidos = candidatos.OrderBy(_ => _rng.Next()).Take(cantidad).ToList();

        int rxOk = 0;
        foreach (var med in elegidos)
        {
            // Días de tratamiento del catálogo si están; si no, duración aleatoria histórica.
            var duracion = med.DiasTratamiento > 0 ? med.DiasTratamiento : Duraciones[_rng.Next(Duraciones.Length)];
            var ok = await PostDrugOrderAsync(patient, med, duracion, ct);
            // La receta expira sola por su duración: queda vigente hasta fecha de visita + duración.
            if (ok) { rxOk++; patient.OrderedConcepts[med.ConceptUuid] = fechaVisita.AddDays(duracion); }
        }
        _logger.LogInformation("[Prescription] {N}/{Total} prescripciones para {Id}",
            rxOk, elegidos.Count, patient.Identifier);
    }

    private async Task<bool> PostDrugOrderAsync(
        SimulatedPatient patient,
        Models.Catalogs.MedicamentoEntry med,
        int duracion,
        CancellationToken ct)
    {
        // Posología del catálogo con fallback al comportamiento histórico (1 tableta / una vez al día):
        // columnas vacías = valores por defecto de Defaults.*, igual que el resto del catálogo.
        var dose      = med.Dosis > 0 ? med.Dosis : 1.0;
        var doseUnits = string.IsNullOrWhiteSpace(med.UnidadDosisUuid)
            ? _settings.Defaults.TabletConceptUuid : med.UnidadDosisUuid;
        var frequency = string.IsNullOrWhiteSpace(med.FrecuenciaUuid)
            ? _settings.Defaults.OnceDailyFrequencyUuid : med.FrecuenciaUuid;

        var payload = new
        {
            type          = "drugorder",
            patient       = patient.OpenMrsUuid,
            concept       = med.ConceptUuid,
            drug          = med.DrugUuid,
            encounter     = patient.ConsultaEncounterUuid,
            orderer       = patient.AssignedProviderUuid ?? _settings.Defaults.ProviderUuid,
            careSetting   = _settings.Defaults.OutpatientCareSettingUuid,
            dose,
            doseUnits,
            route         = med.ViaUuid,
            frequency,
            numRefills    = 0,
            quantity      = (double)duracion,
            quantityUnits = doseUnits,
            duration      = duracion,
            durationUnits = _settings.Defaults.DaysConceptUuid,
            // Sin esto OpenMRS usa el reloj real: la orden quedaba fechada el día de la corrida,
            // no el de la visita simulada (y el autoexpire se calculaba desde hoy). Debe coincidir
            // con el datetime del encounter de consulta (no puede ser anterior a él).
            dateActivated = VisitSeeder.FormatDatetime(ConsultaSeeder.FechaConsulta(patient))
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
