using System.Text.Json;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class ConsultaSeeder
{
    // CIEL concept UUIDs estándar para el encounter de consulta
    private const string DiagnosisCodedUuid      = "1284AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ChiefComplaintUuid      = "162169AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string DiagnosisCertaintyUuid  = "159946AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ConfirmedUuid           = "1065AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PresumptiveUuid         = "1066AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string NormalUuid              = "1115AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string AbnormalUuid            = "1116AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly double _clinicalExamProb;
    private readonly Random _rng = new();

    public ConsultaSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        SimulationSettings simSettings)
    {
        _client          = client;
        _settings        = settings;
        _catalogs        = catalogs;
        _clinicalExamProb = simSettings.ReferralProbabilities.ClinicalExam;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var encounterUuid = await CreateEncounterAsync(patient, ct);
        if (encounterUuid is null) return;
        patient.ConsultaEncounterUuid = encounterUuid;

        // Motivo de consulta (texto libre)
        var motivo = PickMotivoConsulta(patient.Categoria);
        if (!string.IsNullOrEmpty(motivo))
            await PostObsTextAsync(patient.OpenMrsUuid, encounterUuid, ChiefComplaintUuid, motivo, patient.VisitDatetime, ct);

        // Diagnóstico codificado + certeza
        if (patient.Diagnostico is not null)
        {
            await PostObsCodedAsync(patient.OpenMrsUuid, encounterUuid,
                DiagnosisCodedUuid, patient.Diagnostico.CielUuid, patient.VisitDatetime, ct);

            var confirmed = _rng.NextDouble() < 0.70;
            await PostObsCodedAsync(patient.OpenMrsUuid, encounterUuid,
                DiagnosisCertaintyUuid, confirmed ? ConfirmedUuid : PresumptiveUuid, patient.VisitDatetime, ct);
        }

        // Examen en consultorio (si aplica)
        var debeExamen = patient.Diagnostico?.RequiereExamenClinico == true
            ? _rng.NextDouble() < 0.90
            : _rng.NextDouble() < _clinicalExamProb;

        if (debeExamen)
            await SeedExamenClinicoAsync(patient, encounterUuid, ct);
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private async Task<string?> CreateEncounterAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var payload = new
        {
            encounterType     = _settings.Defaults.ConsultaEncounterTypeUuid,
            patient           = patient.OpenMrsUuid,
            visit             = patient.VisitUuid,
            encounterDatetime = VisitSeeder.FormatDatetime(patient.VisitDatetime.AddMinutes(30)),
            location          = _settings.Defaults.LocationUuid,
            encounterProviders = new[]
            {
                new
                {
                    provider      = _settings.Defaults.ProviderUuid,
                    encounterRole = _settings.Defaults.EncounterRoleUuid
                }
            }
        };

        try
        {
            var json = await _client.PostAsync("encounter", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("uuid", out var uuid)) return uuid.GetString();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConsultaSeeder] encounter {patient.Identifier}: {ex.Message}");
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
            await PostObsNumericAsync(patient.OpenMrsUuid, encounterUuid,
                examen.CielUuid, valor, patient.VisitDatetime, ct);
        }
        else
        {
            var valorCoded = _rng.NextDouble() < 0.80 ? NormalUuid : AbnormalUuid;
            await PostObsCodedAsync(patient.OpenMrsUuid, encounterUuid,
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

    private async Task PostObsTextAsync(string personUuid, string encounterUuid,
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
        catch (Exception ex) { Console.Error.WriteLine($"[ConsultaSeeder] obs text: {ex.Message}"); }
    }

    private async Task PostObsCodedAsync(string personUuid, string encounterUuid,
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
        catch (Exception ex) { Console.Error.WriteLine($"[ConsultaSeeder] obs coded: {ex.Message}"); }
    }

    private async Task PostObsNumericAsync(string personUuid, string encounterUuid,
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
        catch (Exception ex) { Console.Error.WriteLine($"[ConsultaSeeder] obs numeric: {ex.Message}"); }
    }
}
