using System.Text.Json;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

public class VitalsSeeder
{
    // UUIDs CIEL estándar para signos vitales en OpenMRS Reference App
    private const string WeightUuid   = "5089AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HeightUuid   = "5090AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SystolicUuid = "5085AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string DiastolicUuid= "5086AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string TempUuid     = "5088AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PulseUuid    = "5087AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SpO2Uuid     = "5242AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly Random _rng = new();

    public VitalsSeeder(OpenMrsRestClient client, OpenMrsSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var encounterUuid = await CreateEncounterAsync(patient, ct);
        if (encounterUuid is null) return;

        var vitals = GenerateVitals(patient);
        foreach (var (conceptUuid, value) in vitals)
        {
            await CreateObsAsync(patient.OpenMrsUuid, encounterUuid, conceptUuid, value, patient.VisitDatetime, ct);
        }
    }

    private async Task<string?> CreateEncounterAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var payload = new
        {
            encounterType      = _settings.Defaults.VitalsEncounterTypeUuid,
            patient            = patient.OpenMrsUuid,
            visit              = patient.VisitUuid,
            encounterDatetime  = VisitSeeder.FormatDatetime(patient.VisitDatetime),
            location           = _settings.Defaults.LocationUuid,
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
            Console.Error.WriteLine($"[VitalsSeeder] encounter {patient.Identifier}: {ex.Message}");
        }
        return null;
    }

    private async Task CreateObsAsync(string personUuid, string encounterUuid,
        string conceptUuid, double value, DateTime obsDatetime, CancellationToken ct)
    {
        var payload = new
        {
            concept      = conceptUuid,
            person       = personUuid,
            encounter    = encounterUuid,
            obsDatetime  = VisitSeeder.FormatDatetime(obsDatetime),
            value        = value
        };

        try { await _client.PostAsync("obs", payload, ct); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VitalsSeeder] obs {conceptUuid}: {ex.Message}");
        }
    }

    private Dictionary<string, double> GenerateVitals(SimulatedPatient patient)
    {
        var cat = patient.Diagnostico?.Categoria ?? patient.Categoria;
        var sev = patient.Diagnostico?.Severidad ?? "";

        // Peso
        var (wMin, wMax) = cat == "diabetes" ? (70.0, 130.0) : (45.0, 120.0);
        var weight = Math.Round(_rng.NextDouble() * (wMax - wMin) + wMin, 1);

        // Talla
        var height = (double)_rng.Next(145, 196);

        // Presión arterial
        var (sysMin, sysMax, diaMin, diaMax) = cat == "cardiovascular"
            ? (140.0, 180.0, 90.0, 110.0)
            : (100.0, 130.0, 60.0,  85.0);
        var systolic  = Math.Round(_rng.NextDouble() * (sysMax - sysMin) + sysMin);
        var diastolic = Math.Round(_rng.NextDouble() * (diaMax - diaMin) + diaMin);

        // Temperatura
        var (tMin, tMax) = cat == "infeccioso" ? (37.5, 39.5) : (36.0, 37.4);
        var temp = Math.Round(_rng.NextDouble() * (tMax - tMin) + tMin, 1);

        // Pulso
        var (pMin, pMax) = cat == "infeccioso" ? (90.0, 115.0) : (60.0, 100.0);
        var pulse = Math.Round(_rng.NextDouble() * (pMax - pMin) + pMin);

        // SpO2 — baja solo en respiratorio grave
        var (sMin, sMax) = (cat == "respiratorio" && sev == "grave") ? (88.0, 94.0) : (96.0, 100.0);
        var spo2 = Math.Round(_rng.NextDouble() * (sMax - sMin) + sMin);

        return new Dictionary<string, double>
        {
            [WeightUuid]    = weight,
            [HeightUuid]    = height,
            [SystolicUuid]  = systolic,
            [DiastolicUuid] = diastolic,
            [TempUuid]      = temp,
            [PulseUuid]     = pulse,
            [SpO2Uuid]      = spo2
        };
    }
}
