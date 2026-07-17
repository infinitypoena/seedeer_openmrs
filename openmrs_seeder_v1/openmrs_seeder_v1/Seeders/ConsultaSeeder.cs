using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class ConsultaSeeder
{
    private const string ChiefComplaintUuid  = "162169AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string NormalUuid          = "1115AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string AbnormalUuid        = "1116AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ReturnVisitDateUuid = "5096AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // Return visit date (datatype Date)

    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly double _clinicalExamProb;
    private readonly ReferralProbabilitiesSettings _referral;
    private readonly RecurrenceSettings _recurrence;
    private readonly Random _rng;
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
        _rng = new Random(simSettings.RandomSeed + 13);
        _referral         = simSettings.ReferralProbabilities;
        _recurrence       = simSettings.Recurrence;
        _logger           = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var encounterUuid = await CreateEncounterAsync(patient, ct);
        if (encounterUuid is null)
        {
            _logger.LogWarning(Eventos.ItemPerdido, "[Consulta] Encounter no creado para {Id} — LabOrder y Prescription se omitirán", patient.Identifier);
            return;
        }
        patient.ConsultaEncounterUuid = encounterUuid;

        // Motivo de consulta (texto libre). Las obs de la consulta se fechan con el datetime del encounter
        // (llegada + 30 min), no con la hora de llegada: OpenMRS no admite obs anteriores a su encounter.
        var fechaConsulta = FechaConsulta(patient);
        var motivo = PickMotivoConsulta(patient.Categoria);
        if (!string.IsNullOrEmpty(motivo))
            await PostObsTextAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid, ChiefComplaintUuid, motivo, fechaConsulta, ct);

        // Examen en consultorio (si aplica)
        var debeExamen = patient.Diagnostico?.RequiereExamenClinico == true
            ? _rng.NextDouble() < 0.90
            : _rng.NextDouble() < _clinicalExamProb;

        if (debeExamen)
            await SeedExamenClinicoAsync(patient, encounterUuid, ct);

        // ── Referencia al hospital ────────────────────────────────────────────────────────────────
        // La clínica es de primer nivel: si el cuadro se le sale de las manos (apendicitis, IAM, sepsis,
        // eclampsia…) no lo trata — lo estabiliza y lo refiere. Es determinista: lo dice la columna
        // 'ambito' del catálogo, no una tirada de dados.
        patient.Referido = ReferenciaPolicy.DebeReferir(patient.TodosDiagnosticos);
        if (patient.Referido)
            await SeedReferenciaAsync(patient, encounterUuid, fechaConsulta, ct);

        // Nota de seguimiento: la probabilidad se condiciona al cuadro (referido ≫ crónico ≫ grave ≫
        // resto) y la fecha sale de la banda clínica que le toca — control post-alta (15–30 d) al
        // referido, banda de recurrencia (crónico mensual/trimestral, agudo 1–3 semanas) al resto. NO un
        // 7–30 días plano. Así la cita coincide con la próxima elegibilidad del paciente y
        // AppointmentSeeder puede agendar la cita real que después gobierna su retorno.
        var esCronico = patient.TodosDiagnosticos.Any(d => d.EsCronica);
        if (_rng.NextDouble() < SeguimientoPolicy.Probabilidad(patient.TodosDiagnosticos, _referral, patient.Referido))
        {
            // El referido vuelve a la clínica tras el alta hospitalaria, no dentro de una semana.
            var fechaCita = patient.Referido
                ? RecurrenceScheduler.ProximaFechaPostReferencia(
                    DateOnly.FromDateTime(patient.VisitDatetime), _rng, _recurrence)
                : RecurrenceScheduler.ProximaFechaElegible(
                    DateOnly.FromDateTime(patient.VisitDatetime), esCronico, _rng, _recurrence);
            var returnDate = fechaCita.ToDateTime(TimeOnly.FromDateTime(patient.VisitDatetime));
            patient.FechaSeguimiento = returnDate;
            await PostObsDateAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
                ReturnVisitDateUuid, returnDate, fechaConsulta, ct);
        }

        _logger.LogInformation("[Consulta] Encounter {Uuid} para {Id} | Dx: {Dx} | comun: {Comun} | +{Comorb} comorbilidad(es){Ref}",
            encounterUuid, patient.Identifier, patient.Diagnostico?.NombreEs ?? "—",
            patient.Diagnostico?.EsComun, patient.Comorbilidades.Count,
            patient.Referido ? " | REFERIDO a hospital" : "");
    }

    /// <summary>
    /// Registra la referencia al hospital de segundo nivel: qué se solicita (remisión a Hospital), el sí
    /// explícito, con qué prisa (Emergencia si el cuadro es grave, Urgente si no) y por qué.
    /// Las cuatro obs cuelgan del encuentro de consulta — en esta instancia no hay encounter type ni
    /// location de referencia (los "Transfer" que existen son traslados internos de cama, ADT).
    /// </summary>
    private async Task SeedReferenciaAsync(
        SimulatedPatient patient, string encounterUuid, DateTime fechaConsulta, CancellationToken ct)
    {
        var dxs = patient.TodosDiagnosticos.ToList();

        await PostObsCodedAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
            ReferenciaPolicy.RemisionesSolicitadasUuid, ReferenciaPolicy.HospitalUuid, fechaConsulta, ct);
        await PostObsCodedAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
            ReferenciaPolicy.ReferidoAHospitalUuid, ReferenciaPolicy.SiUuid, fechaConsulta, ct);
        await PostObsCodedAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
            ReferenciaPolicy.PrioridadReferenciaUuid, ReferenciaPolicy.Prioridad(dxs), fechaConsulta, ct);

        var motivo = ReferenciaPolicy.Motivo(dxs);
        if (!string.IsNullOrEmpty(motivo))
            await PostObsTextAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
                ReferenciaPolicy.MotivoReferenciaUuid, motivo, fechaConsulta, ct);
    }

    /// <summary>
    /// Momento del encounter de consulta (30 min después de la llegada). Las órdenes de lab y
    /// prescripciones deben fechar su <c>dateActivated</c> con ESTE valor: OpenMRS rechaza órdenes
    /// activadas antes que su encounter (<c>Order.error.encounterDatetimeAfterDateActivated</c>).
    /// </summary>
    public static DateTime FechaConsulta(SimulatedPatient patient) => patient.VisitDatetime.AddMinutes(30);

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
            encounterDatetime  = VisitSeeder.FormatDatetime(FechaConsulta(patient)),
            location           = patient.AssignedLocationUuid ?? _settings.Defaults.LocationUuid,
            encounterProviders = new[]
            {
                new
                {
                    provider      = patient.AssignedProviderUuid ?? _settings.Defaults.ProviderUuid,
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
            _logger.LogWarning(Eventos.ItemPerdido, "[Consulta] Encounter sin uuid para {Id}", patient.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Consulta] Error creando encounter para {Id}: {Msg}", patient.Identifier, ex.Message);
        }
        return null;
    }

    private async Task SeedExamenClinicoAsync(SimulatedPatient patient, string encounterUuid, CancellationToken ct)
    {
        // El examen puede corresponder a cualquiera de las categorías del paciente (incluye
        // comorbilidades), igual que labs y fármacos, no solo la categoría primaria.
        var candidatos = _catalogs.ExamenesClinicos
            .Where(e => patient.Categorias.Any(c => AplicaCategoria(e, c)))
            .ToList();

        if (candidatos.Count == 0) return;

        var examen = candidatos[_rng.Next(candidatos.Count)];
        var fechaConsulta = FechaConsulta(patient);

        if (examen.TipoResultado == "numerico")
        {
            var valor = ValorExamenNumerico(examen, _rng);
            await PostObsNumericAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
                examen.CielUuid, valor, fechaConsulta, ct);
        }
        else
        {
            var valorCoded = _rng.NextDouble() < 0.80 ? NormalUuid : AbnormalUuid;
            await PostObsCodedAsync(patient.Identifier, patient.OpenMrsUuid, encounterUuid,
                examen.CielUuid, valorCoded, fechaConsulta, ct);
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
        "neurologico"     => e.AplicaNeurologico,
        "dermatologico"   => e.AplicaDermatologico,
        "salud_mental"    => e.AplicaSaludMental,
        "ginecoobstetrico"=> e.AplicaGinecoobstetrico,
        "trauma"          => e.AplicaTrauma,
        _ => false
    };

    /// <summary>
    /// Seam puro: valor del examen numérico, sorteado en la banda del catálogo (`res_min/res_max`) —
    /// entero cuando los límites son enteros (Glasgow, escala de dolor, FC fetal; mismo criterio de
    /// precisión que LabResultGenerator), 1 decimal si no. La banda es obligatoria para los exámenes
    /// numéricos y la exige <c>CatalogValidator</c> al arranque.
    /// </summary>
    public static double ValorExamenNumerico(Models.Catalogs.ExamenClinicoEntry examen, Random rng)
    {
        var valor = rng.NextDouble() * (examen.ResMax - examen.ResMin) + examen.ResMin;
        var decimales = double.IsInteger(examen.ResMin) && double.IsInteger(examen.ResMax) ? 0 : 1;
        return Math.Round(valor, decimales);
    }

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
        catch (Exception ex) { _logger.LogError(ex, "[Consulta] Error obs texto para {Id}: {Msg}", identifier, ex.Message); }
    }

    private async Task PostObsDateAsync(string identifier, string personUuid, string encounterUuid,
        string conceptUuid, DateTime value, DateTime obsDatetime, CancellationToken ct)
    {
        var payload = new
        {
            concept     = conceptUuid,
            person      = personUuid,
            encounter   = encounterUuid,
            obsDatetime = VisitSeeder.FormatDatetime(obsDatetime),
            value       = VisitSeeder.FormatDatetime(value)
        };
        try { await _client.PostAsync("obs", payload, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[Consulta] Error obs fecha para {Id}: {Msg}", identifier, ex.Message); }
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
        catch (Exception ex) { _logger.LogError(ex, "[Consulta] Error obs coded para {Id}: {Msg}", identifier, ex.Message); }
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
        catch (Exception ex) { _logger.LogError(ex, "[Consulta] Error obs numeric para {Id}: {Msg}", identifier, ex.Message); }
    }
}
