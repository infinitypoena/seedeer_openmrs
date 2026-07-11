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
    private readonly double _labResultProb;
    private readonly Random _rng;
    private readonly ILogger<LabOrderSeeder> _logger;

    public LabOrderSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        SimulationSettings simSettings,
        ILogger<LabOrderSeeder> logger)
    {
        _client        = client;
        _settings      = settings;
        _catalogs      = catalogs;
        _labOrderProb  = simSettings.ReferralProbabilities.LabOrder;
        _urgentProb    = simSettings.ReferralProbabilities.Urgent;
        _labResultProb = simSettings.ReferralProbabilities.LabResult;
        _rng = new Random(simSettings.RandomSeed + 14);
        _logger        = logger;
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

        // Sets para el generador de resultados (categorías + diagnósticos del paciente)
        var categorias = patient.Categorias as ISet<string> ?? new HashSet<string>(patient.Categorias);
        var dxUuids    = patient.TodosDiagnosticos.Select(d => d.CielUuid).ToHashSet();

        int ordenesOk = 0, resultadosOk = 0;
        foreach (var lab in elegidos)
        {
            var esUrgente = patient.TodosDiagnosticos.Any(d => d.Severidad == "grave")
                ? _rng.NextDouble() < 0.50
                : _rng.NextDouble() < _urgentProb;

            var orderUuid = await PostOrderAsync(patient, lab.CielUuid, esUrgente ? "STAT" : "ROUTINE", ct);
            if (orderUuid is null) continue;

            ordenesOk++;
            patient.OrderedConcepts.Add(lab.CielUuid);

            // Resultado el mismo día (fracción _labResultProb); solo numéricos/codificados
            if (_rng.NextDouble() >= _labResultProb) continue;
            var result = LabResultGenerator.Generar(lab, categorias, dxUuids, _rng);
            if (result.Tipo == LabResultGenerator.TipoResultado.Ninguno) continue;
            if (await PostResultObsAsync(patient, lab.CielUuid, orderUuid, result, ct)) resultadosOk++;
        }

        _logger.LogInformation("[LabOrder] {N}/{Total} órdenes + {R} resultados para {Id}",
            ordenesOk, elegidos.Count, resultadosOk, patient.Identifier);
    }

    /// <summary>Crea la orden y devuelve su UUID (o null si falla).</summary>
    private async Task<string?> PostOrderAsync(SimulatedPatient patient, string conceptUuid, string urgency, CancellationToken ct)
    {
        var payload = new
        {
            type        = "testorder",
            patient     = patient.OpenMrsUuid,
            concept     = conceptUuid,
            encounter   = patient.ConsultaEncounterUuid,
            orderer     = patient.AssignedProviderUuid ?? _settings.Defaults.ProviderUuid,
            careSetting = _settings.Defaults.OutpatientCareSettingUuid,
            urgency
        };

        try
        {
            var json = await _client.PostAsync("order", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            return doc.TryGetProperty("uuid", out var uuid) ? uuid.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LabOrder] Error en orden para {Id}: {Msg}", patient.Identifier, ex.Message);
            return null;
        }
    }

    /// <summary>Registra el resultado como obs ligada a la orden (numérico → número; codificado → UUID de respuesta).</summary>
    private async Task<bool> PostResultObsAsync(
        SimulatedPatient patient, string conceptUuid, string orderUuid,
        LabResultGenerator.LabResult result, CancellationToken ct)
    {
        object value = result.Tipo == LabResultGenerator.TipoResultado.Numerico
            ? result.Numerico!.Value
            : result.CodedUuid!;

        var payload = new
        {
            concept     = conceptUuid,
            person      = patient.OpenMrsUuid,
            encounter   = patient.ConsultaEncounterUuid,
            order       = orderUuid,
            obsDatetime = VisitSeeder.FormatDatetime(patient.VisitDatetime),
            value
        };

        try
        {
            await _client.PostAsync("obs", payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LabOrder] Error en resultado {Concept} para {Id}: {Msg}",
                conceptUuid, patient.Identifier, ex.Message);
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
