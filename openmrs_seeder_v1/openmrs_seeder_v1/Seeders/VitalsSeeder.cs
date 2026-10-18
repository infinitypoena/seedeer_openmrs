using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

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
    private readonly Random _rng;
    private readonly ILogger<VitalsSeeder> _logger;

    public VitalsSeeder(OpenMrsRestClient client, OpenMrsSettings settings, SimulationSettings simSettings, ILogger<VitalsSeeder> logger)
    {
        _client   = client;
        _settings = settings;
        _climate  = simSettings.Climate;
        _rng = new Random(simSettings.RandomSeed + 12);
        _logger   = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var encounterUuid = await CreateEncounterAsync(patient, ct);
        if (encounterUuid is null)
        {
            _logger.LogWarning(Eventos.ItemPerdido, "[Vitals] Skip obs: encounter no creado para {Id}", patient.Identifier);
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
            _logger.LogWarning(Eventos.ItemPerdido, "[Vitals] Encounter sin uuid para {Id}", patient.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Vitals] Error creando encounter para {Id}: {Msg}", patient.Identifier, ex.Message);
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
            _logger.LogError(ex, "[Vitals] Error en obs {Concept} para {Id}: {Msg}", conceptUuid, identifier, ex.Message);
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
        var paOverride   = dxs.Any(d => d.VitalPa == "alta") ? "alta" : null;
        var fcOverride   = dxs.Any(d => d.VitalFc == "alta") ? "alta"
                         : dxs.Any(d => d.VitalFc == "baja") ? "baja"
                         : null;
        var spo2Override = dxs.Any(d => d.VitalSpo2 == "baja") ? "baja" : null;

        var edadMeses = PatientProfileGenerator.EdadEnMeses(
            patient.BirthDate, DateOnly.FromDateTime(patient.VisitDatetime));

        var v = ComputeVitals(patient.Categorias, severityRank, patient.Gender, patient.AgeGroup,
            fiebre, imcOverride, patient.TempAmbienteC, _climate, _rng,
            paOverride, fcOverride, spo2Override,
            patient.TallaCm, patient.ImcBasal, edadMeses);

        // Fijar la antropometría basal en la primera visita; las recurrentes la heredan (talla constante,
        // peso derivando poco alrededor del IMC basal).
        patient.TallaCm  ??= v.HeightCm;
        patient.ImcBasal ??= Math.Round(v.WeightKg / Math.Pow(v.HeightCm / 100.0, 2), 1);

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

    // Mediana WHO de talla para la edad (0–14 años), por sexo: anclas en meses → cm.
    // Fuente: estándares de crecimiento OMS (talla-para-edad, percentil 50), redondeados a 1 decimal.
    private static readonly (int Meses, double Nino, double Nina)[] AnclasWhoTalla = new[]
    {
        (0,   49.9,  49.1),
        (6,   67.6,  65.7),
        (12,  75.7,  74.0),
        (24,  87.1,  85.7),
        (36,  96.1,  95.1),
        (60,  110.0, 109.4),
        (120, 137.8, 138.6),
        (168, 163.2, 159.4)
    };

    // Desviación típica pediátrica (cm) que crece con la edad: ~2 cm al nacer → ~7 cm en la pubertad.
    private static readonly (int Meses, double Sigma)[] SigmaPediatricaTalla = new[]
    {
        (0,   2.0),
        (12,  2.8),
        (36,  3.8),
        (60,  4.5),
        (120, 5.5),
        (168, 7.0)
    };

    private const double TallaAdultoHombreMedia = 170.0;
    private const double TallaAdultoMujerMedia = 158.0;
    // σ amplia a propósito: 200 cm (2 m) queda a +3.75σ del hombre, alcanzable pero raro (~1 de cada
    // 11.000); con la σ histórica de 6.5 era +4.6σ y jamás salía (máx observado 188). La mujer queda a
    // +4.6σ de su tope de 190 (rarísimo), manteniendo la dispersión femenina algo menor que la masculina.
    private const double TallaAdultoHombreSigma = 8.0;
    private const double TallaAdultoMujerSigma = 7.0;

    /// <summary>
    /// Mediana teórica de talla (cm): WHO talla-para-edad por sexo para 0–14 años; media poblacional
    /// adulta por sexo fuera de esa franja. Sustituye a la antigua curva lineal sin distinción de sexo.
    /// </summary>
    public static double TallaTeoricaMedianaCm(string genero, int? edadMeses)
    {
        if (edadMeses is int m && m <= 14 * 12)
            return InterpolarWhoTalla(m, genero == "M");
        return genero == "M" ? TallaAdultoHombreMedia : TallaAdultoMujerMedia;
    }

    /// <summary>Desviación típica de la talla (cm) por edad/sexo: crece con la edad en pediatría.</summary>
    public static double SigmaTallaCm(string genero, int? edadMeses)
    {
        if (edadMeses is int m && m <= 14 * 12)
            return Interpolar(m, SigmaPediatricaTalla);
        return genero == "M" ? TallaAdultoHombreSigma : TallaAdultoMujerSigma;
    }

    /// <summary>
    /// Pérdida de talla por envejecimiento (cm): 0 hasta los 50 años, luego ~0,5 cm por década, tope 3 cm.
    /// </summary>
    public static double DecrementoTallaSenilCm(int edadMeses)
    {
        var edad = edadMeses / 12.0;
        return Math.Clamp((edad - 50.0) * 0.05, 0.0, 3.0);
    }

    private static double InterpolarWhoTalla(int meses, bool nino)
    {
        if (meses <= AnclasWhoTalla[0].Meses) return Val(AnclasWhoTalla[0]);
        for (int i = 1; i < AnclasWhoTalla.Length; i++)
        {
            if (meses <= AnclasWhoTalla[i].Meses)
            {
                var t = (meses - AnclasWhoTalla[i - 1].Meses) / (double)(AnclasWhoTalla[i].Meses - AnclasWhoTalla[i - 1].Meses);
                return Val(AnclasWhoTalla[i - 1]) + t * (Val(AnclasWhoTalla[i]) - Val(AnclasWhoTalla[i - 1]));
            }
        }
        return Val(AnclasWhoTalla[^1]);

        double Val((int Meses, double Nino, double Nina) a) => nino ? a.Nino : a.Nina;
    }

    private static double Interpolar(int x, (int Meses, double Sigma)[] tabla)
    {
        if (x <= tabla[0].Meses) return tabla[0].Sigma;
        for (int i = 1; i < tabla.Length; i++)
        {
            if (x <= tabla[i].Meses)
            {
                var t = (x - tabla[i - 1].Meses) / (double)(tabla[i].Meses - tabla[i - 1].Meses);
                return tabla[i - 1].Sigma + t * (tabla[i].Sigma - tabla[i - 1].Sigma);
            }
        }
        return tabla[^1].Sigma;
    }

    /// <summary>Normal (media, σ) por Box-Muller, mismo método que SatisfaccionPolicy/DailyScheduleGenerator.</summary>
    private static double Normal(Random rng, double media, double sigma)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return media + sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

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
        Random rng,
        string? paOverride = null,
        string? fcOverride = null,
        string? spo2Override = null,
        double? tallaFijaCm = null,
        double? imcBasal = null,
        int? edadMeses = null)
    {
        var cats = categorias as ISet<string> ?? new HashSet<string>(categorias);
        bool Has(string c) => cats.Contains(c);
        bool esNino = ageGroup == "0-14";

        double Rand(double min, double max) => rng.NextDouble() * (max - min) + min;

        // ── Talla (primero; el peso depende de ella) ──
        // Fijada (visitas siguientes) → constante. Si no: normal sobre la mediana teórica (WHO talla-para-edad
        // por sexo en pediatría; media poblacional por sexo en adultos), con σ por edad y decremento senil en
        // adultos mayores. Siempre a 1 decimal (como se registra en clínica): así los bebés no se amontonan en
        // enteros (70, 77, 78…) y un hombre puede llegar a los 2 m (σ=8 → 200 cm = +3.75σ, raro pero posible).
        double height;
        if (tallaFijaCm is double tf)
        {
            height = tf;
        }
        else if (esNino && edadMeses is int em)
        {
            var mediana = TallaTeoricaMedianaCm(gender, em);
            var sigma   = SigmaTallaCm(gender, em);
            height = Math.Round(Math.Clamp(Normal(rng, mediana, sigma), 45, 190), 1);
        }
        else if (esNino)
        {
            // Fallback 0-14 sin edad en meses (no ocurre en producción: BuildVitalsObs siempre la calcula).
            height = Math.Round(Rand(90, 160), 1);
        }
        else
        {
            var mediana = TallaTeoricaMedianaCm(gender, null);
            var sigma   = SigmaTallaCm(gender, null);
            var (min, max) = gender == "M" ? (150.0, 200.0) : (140.0, 190.0);
            height = Math.Round(Math.Clamp(Normal(rng, mediana, sigma), min, max), 1);
            if (edadMeses is int emAdulta)
                height = Math.Round(height - DecrementoTallaSenilCm(emAdulta), 1);
        }

        // ── Peso vía IMC objetivo (coherente con talla) ──
        // El override por enfermedad (vital_imc) tiene prioridad sobre la categoría.
        var (imcMin, imcMax) =
            imcOverride == "alto"                  ? (27.0, 38.0)
          : imcOverride == "bajo"                  ? (16.0, 19.0)
          : Has("diabetes") || Has("endocrino")    ? (27.0, 38.0)
          : esNino                                 ? (14.0, 20.0)
          :                                          (18.5, 27.0);
        // Con IMC basal (visitas siguientes) y sin efecto puntual de enfermedad, el IMC deriva poco
        // alrededor del basal; si hay override de enfermedad, o es la primera visita, manda la banda.
        var imc = imcBasal is double ib && imcOverride is null
            ? Math.Clamp(ib + Rand(-1.5, 1.5), 14.0, 40.0)
            : Rand(imcMin, imcMax);
        var weight = Math.Round(imc * Math.Pow(height / 100.0, 2), 1);

        // ── Presión arterial (el override por enfermedad gana sobre la categoría) ──
        var (sysMin, sysMax, diaMin, diaMax) = paOverride == "alta" || Has("cardiovascular")
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

        // ── Pulso (el override por enfermedad gana; si no: taquicardia con fiebre o cardiopatía) ──
        var (pMin, pMax) = fcOverride == "alta" ? (100.0, 130.0)
                         : fcOverride == "baja" ? (42.0, 58.0)
                         : febril ? (90.0, 120.0)
                         : Has("cardiovascular") ? (80.0, 110.0)
                         : (60.0, 100.0);
        var pulse = Math.Round(Rand(pMin, pMax));

        // ── Frecuencia respiratoria (taquipnea en respiratorio/infeccioso); acotada ≤99 ──
        var (frMin, frMax) = (Has("respiratorio") || Has("infeccioso"))
            ? (20.0, severityRank >= 3 ? 34.0 : 30.0)
            : (12.0, 20.0);
        var respRate = Math.Min(99.0, Math.Round(Rand(frMin, frMax)));

        // ── SpO2 (el override por enfermedad gana; si no: baja según severidad respiratoria); concepto 5092 ──
        var (sMin, sMax) = spo2Override == "baja" ? (88.0, 94.0)
                         : Has("respiratorio") && severityRank >= 3 ? (88.0, 93.0)
                         : Has("respiratorio") && severityRank >= 2 ? (92.0, 96.0)
                         : (95.0, 99.0);
        var spo2 = Math.Round(Rand(sMin, sMax));

        return new VitalSigns(weight, height, systolic, diastolic, temp, pulse, respRate, spo2);
    }
}
