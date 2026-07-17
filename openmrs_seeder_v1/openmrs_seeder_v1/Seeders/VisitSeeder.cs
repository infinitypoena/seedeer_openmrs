using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class VisitSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly ILogger<VisitSeeder> _logger;

    public VisitSeeder(OpenMrsRestClient client, OpenMrsSettings settings, ILogger<VisitSeeder> logger)
    {
        _client   = client;
        _settings = settings;
        _logger   = logger;
    }

    public async Task<string?> CreateAsync(SimulatedPatient patient, CancellationToken ct)
    {
        var payload = new
        {
            patient       = patient.OpenMrsUuid,
            visitType     = _settings.Defaults.VisitTypeUuid,
            startDatetime = FormatDatetime(patient.VisitDatetime),
            location      = patient.AssignedLocationUuid ?? _settings.Defaults.LocationUuid
        };

        try
        {
            var json = await _client.PostAsync("visit", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("uuid", out var uuidProp))
            {
                var uuid = uuidProp.GetString();
                _logger.LogInformation("[Visit] Creada {VisitUuid} para {Id} ({Date})",
                    uuid, patient.Identifier, patient.VisitDatetime.ToString("yyyy-MM-dd"));
                return uuid;
            }
            _logger.LogWarning(Eventos.ItemPerdido, "[Visit] Respuesta sin uuid para {Id}", patient.Identifier);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("visitCannotOverlapAnother"))
        {
            // Patient has an unclosed visit from a previous day — reuse the active one
            var existingUuid = await GetActiveVisitUuidAsync(patient.OpenMrsUuid, ct);
            if (existingUuid is not null)
            {
                _logger.LogWarning("[Visit] Visita activa reutilizada {Uuid} para {Id} ({Date})",
                    existingUuid, patient.Identifier, patient.VisitDatetime.ToString("yyyy-MM-dd"));
                return existingUuid;
            }
            _logger.LogError("[Visit] Overlap y sin visita activa recuperable para {Id}", patient.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Visit] Error para {Id} ({Date}): {Msg}",
                patient.Identifier, patient.VisitDatetime.ToString("yyyy-MM-dd"), ex.Message);
        }
        return null;
    }

    private async Task<string?> GetActiveVisitUuidAsync(string patientUuid, CancellationToken ct)
    {
        try
        {
            var json = await _client.GetAsync(
                $"visit?patient={patientUuid}&includeInactive=false&v=default", ct);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                return results[0].GetProperty("uuid").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Visit] Error buscando visita activa para {Patient}: {Msg}", patientUuid, ex.Message);
        }
        return null;
    }

    /// <summary>
    /// Offset UTC adjuntado a TODA fecha enviada a OpenMRS (formato "±HHmm"). Se fija una vez al
    /// arranque desde <c>Simulation.UtcOffset</c> (Program.cs). "+0000" = UTC (comportamiento
    /// histórico); "-0600" = hora de El Salvador, para que las horas se lean como hora local en la
    /// UI cuando el backend tiene TZ America/El_Salvador.
    /// </summary>
    public static string UtcOffset { get; set; } = "+0000";

    public static string FormatDatetime(DateTime dt) =>
        dt.ToString("yyyy-MM-dd'T'HH:mm:ss.000") + UtcOffset;

    /// <summary>Seam puro: normaliza el offset de config ("-06:00", "-0600", "" → "±HHmm").</summary>
    public static string NormalizarOffset(string? offset)
    {
        if (string.IsNullOrWhiteSpace(offset)) return "+0000";
        var limpio = offset.Trim().Replace(":", "");
        if (!System.Text.RegularExpressions.Regex.IsMatch(limpio, @"^[+-]\d{4}$"))
            throw new FormatException($"UtcOffset inválido: '{offset}' (esperado ±HH:mm, p.ej. -06:00)");
        return limpio;
    }
}
