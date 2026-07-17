using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Asegura de forma <b>idempotente</b> que un profesional exista en OpenMRS: lo busca por su
/// identificador y, si no está, crea su <c>person</c> + <c>provider</c>. Devuelve el UUID del provider
/// (o <c>null</c> si el REST falló).
/// <para>
/// Es la pieza compartida por <see cref="ClinicResourceAssigner"/> (médicos <c>SIM-MED-*</c>) y
/// <see cref="LabStaffAssigner"/> (laboratorio <c>SIM-LAB-*</c>): ambos son datos de referencia que se
/// reutilizan entre corridas y que el subcomando <c>clear</c> NO anula.
/// </para>
/// </summary>
public class ProviderEnsurer
{
    private readonly OpenMrsRestClient _client;
    private readonly ILogger<ProviderEnsurer> _logger;

    public ProviderEnsurer(OpenMrsRestClient client, ILogger<ProviderEnsurer> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>UUID del provider con ese identificador; lo crea (person + provider) si no existe.</summary>
    public async Task<string?> EnsureAsync(string identifier, string nombre, string genero, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        try
        {
            var existing = await FindProviderUuidAsync(identifier, ct);
            if (existing is not null)
            {
                _logger.LogDebug("[Provider] {Id} ya existe ({Uuid})", identifier, existing);
                return existing;
            }

            var (given, family) = SplitNombre(nombre, identifier);

            var personJson = await _client.PostAsync("person", new
            {
                names  = new[] { new { givenName = given, familyName = family, preferred = true } },
                gender = string.IsNullOrWhiteSpace(genero) ? "M" : genero
            }, ct);
            var personUuid = JsonSerializer.Deserialize<JsonElement>(personJson).GetProperty("uuid").GetString();

            var providerJson = await _client.PostAsync("provider", new
            {
                person     = personUuid,
                identifier
            }, ct);
            var providerUuid = JsonSerializer.Deserialize<JsonElement>(providerJson).GetProperty("uuid").GetString();

            _logger.LogInformation("[Provider] Creado {Nombre} ({Id}) → {Uuid}", nombre, identifier, providerUuid);
            return providerUuid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Provider] Error asegurando {Id}: {Msg}", identifier, ex.Message);
            return null;
        }
    }

    private async Task<string?> FindProviderUuidAsync(string identifier, CancellationToken ct)
    {
        var json = await _client.GetAsync(
            $"provider?q={Uri.EscapeDataString(identifier)}&v=custom:(uuid,identifier)", ct);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        if (!doc.TryGetProperty("results", out var results)) return null;

        foreach (var p in results.EnumerateArray())
        {
            if (p.TryGetProperty("identifier", out var idProp) &&
                string.Equals(idProp.GetString(), identifier, StringComparison.OrdinalIgnoreCase))
                return p.GetProperty("uuid").GetString();
        }
        return null;
    }

    /// <summary>Parte "Ana Beatriz Portillo" en (nombre, apellidos). Seam puro para los tests.</summary>
    public static (string Given, string Family) SplitNombre(string nombre, string fallback)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return (fallback, "Profesional");
        var parts = nombre.Trim().Split(' ', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], "Profesional");
    }
}
