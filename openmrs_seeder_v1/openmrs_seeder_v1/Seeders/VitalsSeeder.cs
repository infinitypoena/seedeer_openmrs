using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

/// <summary>Signos vitales coherentes con la(s) enfermedad(es) del paciente.</summary>
public class VitalsSeeder
{
    private const string WeightUuid    = "5089AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HeightUuid    = "5090AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SystolicUuid  = "5085AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string DiastolicUuid = "5086AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string TempUuid      = "5088AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PulseUuid     = "5087AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string RespRateUuid  = "5242AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // Frecuencia respiratoria (hiAbsolute=99)
    private const string SpO2Uuid      = "5092AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // Saturación periférica de oxígeno (hiAbsolute=100)

    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly ClimateSettings _climate;
    private readonly Random _rng = new();
    private readonly ILogger<VitalsSeeder> _logger;

    public VitalsSeeder(OpenMrsRestClient client, OpenMrsSettings settings, SimulationSettings simSettings, ILogger<VitalsSeeder> logger)
    {
        _client   = client;
        _settings = settings;
        _climate  = simSettings.Climate;
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

        var vitals  = BuildVitalsObs(patient);
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
            location           = patient.AssignedLocationUuid ?? _settings.Defaults.LocationUuid,
            encounterProviders = new[]
            {
                new
                {
                    provider      = patient.AssignedProviderUuid ?? _settings.Defaults.ProviderUuid,
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

    /// <summary>Construye los inputs desde el paciente, llama al seam puro y mapea a {conceptUuid → valor}.</summary>
    private Dictionary<string, double> BuildVitalsObs(SimulatedPatient patient)
    {
        var dxs          = patient.TodosDiagnosticos.ToList();
        var severityRank = dxs.Count == 0 ? 1 : dxs.Max(d => SeverityRank(d.Severidad));
        var fiebre       = dxs.Any(d => d.VitalFiebre);
        var imcOverride  = dxs.Any(d => d.VitalImc == "alto") ? "alto"
                         : dxs.Any(d => d.VitalImc == "bajo") ? "bajo"
                         : null;

        var v = ComputeVitals(patient.Categorias, severityRank, patient.Gender, patient.AgeGroup,
            fiebre, imcOverride, patient.TempAmbienteC, _climate, _rng);

        return new Dictionary<string, double>
        {
            [WeightUuid]    = v.WeightKg,
            [HeightUuid]    = v.HeightCm,
            [SystolicUuid]  = v.Systolic,
            [DiastolicUuid] = v.Diastolic,
            [TempUuid]      = v.TempC,
            [PulseUuid]     = v.Pulse,
            [RespRateUuid]  = v.RespRate,
            [SpO2Uuid]      = v.SpO2
        };
    }

    /// <summary>Vitales como valores nombrados (resultado del seam puro).</summary>
    public readonly record struct VitalSigns(
        double WeightKg, double HeightCm, double Systolic, double Diastolic,
        double TempC, double Pulse, double RespRate, double SpO2);

    /// <summary>leve=1, moderado=2, grave=3 (default 1).</summary>
    public static int SeverityRank(string? severidad) => (severidad ?? "").ToLowerInvariant() switch
    {
        "grave"    => 3,
        "moderado" => 2,
        _          => 1
    };

    /// <summary>
    /// Seam puro y testeable: deriva los signos vitales a partir de la UNIÓN de categorías del
    /// paciente (incluye comorbilidades), la severidad y overrides opcionales por enfermedad.
    /// El peso se acopla a la talla vía IMC para que sea clínicamente coherente.
    /// </summary>
    public static VitalSigns ComputeVitals(
        IEnumerable<string> categorias,
        int severityRank,
        string gender,
        string ageGroup,
        bool fiebreForzada,
        string? imcOverride,
        double? tempAmbienteC,
        ClimateSettings climate,
        Random rng)
    {
        var cats = categorias as ISet<string> ?? new HashSet<string>(categorias);
        bool Has(string c) => cats.Contains(c);
        bool esNino = ageGroup == "0-14";

        double Rand(double min, double max) => rng.NextDouble() * (max - min) + min;

        // ── Talla (primero; el peso depende de ella) ──
        double height = esNino
            ? Math.Round(Rand(90, 160))
            : Math.Round(gender == "M" ? Rand(160, 185) : Rand(150, 172));

        // ── Peso vía IMC objetivo (coherente con talla) ──
        // El override por enfermedad (vital_imc) tiene prioridad sobre la categoría.
        var (imcMin, imcMax) =
            imcOverride == "alto"                  ? (27.0, 38.0)
          : imcOverride == "bajo"                  ? (16.0, 19.0)
          : Has("diabetes") || Has("endocrino")    ? (27.0, 38.0)
          : esNino                                 ? (14.0, 20.0)
          :                                          (18.5, 27.0);
        var imc    = Rand(imcMin, imcMax);
        var weight = Math.Round(imc * Math.Pow(height / 100.0, 2), 1);

        // ── Presión arterial ──
        var (sysMin, sysMax, diaMin, diaMax) = Has("cardiovascular")
            ? (140.0, 180.0, 90.0, 110.0)
            : (100.0, 130.0, 60.0,  85.0);
        var systolic  = Math.Round(Rand(sysMin, sysMax));
        var diastolic = Math.Round(Rand(diaMin, diaMax));

        // ── Temperatura (fiebre por enfermedad febril; el calor ambiental la sube un poco) ──
        bool febril = fiebreForzada || Has("infeccioso") || (Has("respiratorio") && severityRank >= 2);
        var (tMin, tMax) = febril ? (37.5, severityRank >= 3 ? 40.0 : 39.5) : (36.0, 37.4);
        var temp = Rand(tMin, tMax);
        if (tempAmbienteC is double ambiente && ambiente > climate.ComfortTempC)
            temp += Math.Min(climate.TempVitalsMaxC, (ambiente - climate.ComfortTempC) * climate.TempVitalsFactorC);
        temp = Math.Round(temp, 1);

        // ── Pulso (taquicardia con fiebre o cardiopatía) ──
        var (pMin, pMax) = febril ? (90.0, 120.0)
                         : Has("cardiovascular") ? (80.0, 110.0)
                         : (60.0, 100.0);
        var pulse = Math.Round(Rand(pMin, pMax));

        // ── Frecuencia respiratoria (taquipnea en respiratorio/infeccioso); acotada ≤99 ──
        var (frMin, frMax) = (Has("respiratorio") || Has("infeccioso"))
            ? (20.0, severityRank >= 3 ? 34.0 : 30.0)
            : (12.0, 20.0);
        var respRate = Math.Min(99.0, Math.Round(Rand(frMin, frMax)));

        // ── SpO2 (baja según severidad respiratoria); concepto 5092 (hiAbsolute=100) ──
        var (sMin, sMax) = Has("respiratorio") && severityRank >= 3 ? (88.0, 93.0)
                         : Has("respiratorio") && severityRank >= 2 ? (92.0, 96.0)
                         : (95.0, 99.0);
        var spo2 = Math.Round(Rand(sMin, sMax));

        return new VitalSigns(weight, height, systolic, diastolic, temp, pulse, respRate, spo2);
    }
}
