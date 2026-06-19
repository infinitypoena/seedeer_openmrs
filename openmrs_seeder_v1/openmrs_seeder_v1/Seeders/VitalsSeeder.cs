using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

public class VitalsSeeder
{
    private const string WeightUuid    = "5089AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HeightUuid    = "5090AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SystolicUuid  = "5085AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string DiastolicUuid = "5086AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string TempUuid      = "5088AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PulseUuid     = "5087AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SpO2Uuid      = "5242AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly Random _rng = new();
    private readonly ILogger<VitalsSeeder> _logger;

    public VitalsSeeder(OpenMrsRestClient client, OpenMrsSettings settings, ILogger<VitalsSeeder> logger)
    {
        _client   = client;
        _settings = settings;
        _logger   = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var encounterUuid = await CreateEncounterAsync(patient, ct);
        if (encounterUuid is null)
        {
            _logger.LogWarning("[Vitals] Skip obs: encounter no creado para {Id}", patient.Identifier);
            return;
        }

        var vitals  = GenerateVitals(patient);
        int obsOk   = 0;
        foreach (var (conceptUuid, value) in vitals)
        {
            var ok = await CreateObsAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid, conceptUuid, value, patient.VisitDatetime, ct);
            if (ok) obsOk++;
        }
        _logger.LogInformation("[Vitals] Encounter {Uuid} + {N}/{Total} obs para {Id}",
            encounterUuid, obsOk, vitals.Count, patient.Identifier);
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
            _logger.LogWarning("[Vitals] Encounter sin uuid para {Id}", patient.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Vitals] Error creando encounter para {Id}: {Msg}", patient.Identifier, ex.Message);
        }
        return null;
    }

    private async Task<bool> CreateObsAsync(string identifier, string personUuid, string encounterUuid,
        string conceptUuid, double value, DateTime obsDatetime, CancellationToken ct)
    {
        var payload = new
        {
            concept     = conceptUuid,
            person      = personUuid,
            encounter   = encounterUuid,
            obsDatetime = VisitSeeder.FormatDatetime(obsDatetime),
            value
        };

        try
        {
            await _client.PostAsync("obs", payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Vitals] Error en obs {Concept} para {Id}: {Msg}", conceptUuid, identifier, ex.Message);
            return false;
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

        // SpO2 — baja solo en respiratorio grave (hiAbsolute=99 en esta instancia)
        var (sMin, sMax) = (cat == "respiratorio" && sev == "grave") ? (88.0, 93.0) : (95.0, 98.0);
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
