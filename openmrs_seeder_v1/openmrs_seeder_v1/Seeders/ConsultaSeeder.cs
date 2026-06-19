using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class ConsultaSeeder
{
    private const string ChiefComplaintUuid = "162169AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string NormalUuid         = "1115AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string AbnormalUuid       = "1116AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly double _clinicalExamProb;
    private readonly Random _rng = new();
    private readonly ILogger<ConsultaSeeder> _logger;

    public ConsultaSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        SimulationSettings simSettings,
        ILogger<ConsultaSeeder> logger)
    {
        _client           = client;
        _settings         = settings;
        _catalogs         = catalogs;
        _clinicalExamProb = simSettings.ReferralProbabilities.ClinicalExam;
        _logger           = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var encounterUuid = await CreateEncounterAsync(patient, ct);
        if (encounterUuid is null)
        {
            _logger.LogWarning("[Consulta] Encounter no creado para {Id} — LabOrder y Prescription se omitirán", patient.Identifier);
            return;
        }
        patient.ConsultaEncounterUuid = encounterUuid;

        // Motivo de consulta (texto libre)
        var motivo = PickMotivoConsulta(patient.Categoria);
        if (!string.IsNullOrEmpty(motivo))
            await PostObsTextAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid, ChiefComplaintUuid, motivo, patient.VisitDatetime, ct);

        // Examen en consultorio (si aplica)
        var debeExamen = patient.Diagnostico?.RequiereExamenClinico == true
            ? _rng.NextDouble() < 0.90
            : _rng.NextDouble() < _clinicalExamProb;

        if (debeExamen)
            await SeedExamenClinicoAsync(patient, encounterUuid, ct);

        _logger.LogInformation("[Consulta] Encounter {Uuid} para {Id} | Dx: {Dx} | +{Comorb} comorbilidad(es)",
            encounterUuid, patient.Identifier, patient.Diagnostico?.NombreEs ?? "—", patient.Comorbilidades.Count);
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private async Task<string?> CreateEncounterAsync(SimulatedPatient patient, CancellationToken ct)
    {
        // Primario rank=1, comorbilidades rank=2; cada Dx con su propia certeza.
        var diagnoses = patient.TodosDiagnosticos
            .Select((dx, i) => (object)new
            {
                rank      = i == 0 ? 1 : 2,
                certainty = _rng.NextDouble() < 0.70 ? "CONFIRMED" : "PROVISIONAL",
                diagnosis = new { coded = dx.CielUuid }
            })
            .ToArray();

        var payload = new
        {
            encounterType      = _settings.Defaults.ConsultaEncounterTypeUuid,
            patient            = patient.OpenMrsUuid,
            visit              = patient.VisitUuid,
            encounterDatetime  = VisitSeeder.FormatDatetime(patient.VisitDatetime.AddMinutes(30)),
            location           = _settings.Defaults.LocationUuid,
            encounterProviders = new[]
            {
                new
                {
                    provider      = _settings.Defaults.ProviderUuid,
                    encounterRole = _settings.Defaults.EncounterRoleUuid
                }
            },
            diagnoses = diagnoses.Length == 0 ? null : diagnoses
        };

        try
        {
            var json = await _client.PostAsync("encounter", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("uuid", out var uuid)) return uuid.GetString();
            _logger.LogWarning("[Consulta] Encounter sin uuid para {Id}", patient.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Consulta] Error creando encounter para {Id}: {Msg}", patient.Identifier, ex.Message);
        }
        return null;
    }

    private async Task SeedExamenClinicoAsync(SimulatedPatient patient, string encounterUuid, CancellationToken ct)
    {
        var candidatos = _catalogs.ExamenesClinicos
            .Where(e => AplicaCategoria(e, patient.Categoria))
            .ToList();

        if (candidatos.Count == 0) return;

        var examen = candidatos[_rng.Next(candidatos.Count)];

        if (examen.TipoResultado == "numerico")
        {
            var valor = GenerateNumericValue(examen.Unidad, patient.Categoria);
            await PostObsNumericAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
                examen.CielUuid, valor, patient.VisitDatetime, ct);
        }
        else
        {
            var valorCoded = _rng.NextDouble() < 0.80 ? NormalUuid : AbnormalUuid;
            await PostObsCodedAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
                examen.CielUuid, valorCoded, patient.VisitDatetime, ct);
        }
    }

    private string? PickMotivoConsulta(string categoria)
    {
        var opciones = _catalogs.MotivosConsulta
            .Where(m => m.Categoria == categoria)
            .ToList();
        if (opciones.Count == 0) return null;
        return opciones[_rng.Next(opciones.Count)].Texto;
    }

    private static bool AplicaCategoria(Models.Catalogs.ExamenClinicoEntry e, string cat) => cat switch
    {
        "respiratorio"   => e.AplicaRespiratorio,
        "cardiovascular" => e.AplicaCardiovascular,
        "diabetes"       => e.AplicaDiabetes,
        "digestivo"      => e.AplicaDigestivo,
        "osteomuscular"  => e.AplicaOsteomuscular,
        "urologico"      => e.AplicaUrologico,
        "infeccioso"     => e.AplicaInfeccioso,
        "endocrino"      => e.AplicaEndocrino,
        _ => false
    };

    private double GenerateNumericValue(string unidad, string categoria) => unidad switch
    {
        "mg/dL" => categoria == "diabetes"
            ? Math.Round(_rng.NextDouble() * 200 + 100, 1)  // 100-300 en diabéticos
            : Math.Round(_rng.NextDouble() * 60  + 70,  1), // 70-130 normal
        "%" => categoria == "respiratorio"
            ? Math.Round(_rng.NextDouble() * 8 + 88, 1)     // 88-96 en respiratorio
            : Math.Round(_rng.NextDouble() * 5 + 95, 1),    // 95-100 normal
        "mmHg" => Math.Round(_rng.NextDouble() * 80 + 100), // 100-180
        _      => Math.Round(_rng.NextDouble() * 0.7 + 0.6, 2) // 0.6-1.3 (ITB)
    };

    private async Task PostObsTextAsync(string identifier, string personUuid, string encounterUuid,
        string conceptUuid, string text, DateTime dt, CancellationToken ct)
    {
        var payload = new
        {
            concept     = conceptUuid,
            person      = personUuid,
            encounter   = encounterUuid,
            obsDatetime = VisitSeeder.FormatDatetime(dt),
            value       = text
        };
        try { await _client.PostAsync("obs", payload, ct); }
        catch (Exception ex) { _logger.LogError("[Consulta] Error obs texto para {Id}: {Msg}", identifier, ex.Message); }
    }

    private async Task PostObsCodedAsync(string identifier, string personUuid, string encounterUuid,
        string conceptUuid, string valueConceptUuid, DateTime dt, CancellationToken ct)
    {
        var payload = new
        {
            concept     = conceptUuid,
            person      = personUuid,
            encounter   = encounterUuid,
            obsDatetime = VisitSeeder.FormatDatetime(dt),
            value       = valueConceptUuid
        };
        try { await _client.PostAsync("obs", payload, ct); }
        catch (Exception ex) { _logger.LogError("[Consulta] Error obs coded para {Id}: {Msg}", identifier, ex.Message); }
    }

    private async Task PostObsNumericAsync(string identifier, string personUuid, string encounterUuid,
        string conceptUuid, double value, DateTime dt, CancellationToken ct)
    {
        var payload = new
        {
            concept     = conceptUuid,
            person      = personUuid,
            encounter   = encounterUuid,
            obsDatetime = VisitSeeder.FormatDatetime(dt),
            value
        };
        try { await _client.PostAsync("obs", payload, ct); }
        catch (Exception ex) { _logger.LogError("[Consulta] Error obs numeric para {Id}: {Msg}", identifier, ex.Message); }
    }
}
