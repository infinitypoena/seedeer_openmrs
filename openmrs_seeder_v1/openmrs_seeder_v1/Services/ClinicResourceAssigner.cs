using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Reparte las visitas entre los consultorios configurados, cada uno con su médico (provider).
/// Al inicio de la corrida asegura — de forma idempotente — que cada médico exista en OpenMRS
/// (buscándolo por identificador y creándolo si falta). Luego <see cref="Assign"/> devuelve un
/// par (ubicación, médico) aleatorio por visita. Si no hay consultorios configurados, cae a los
/// valores por defecto (LocationUuid/ProviderUuid), conservando el comportamiento previo.
/// </summary>
public class ClinicResourceAssigner
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly ILogger<ClinicResourceAssigner> _logger;
    private readonly Random _rng = new();

    private List<(string Location, string Provider)> _consultorios = [];

    public ClinicResourceAssigner(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        ILogger<ClinicResourceAssigner> logger)
    {
        _client   = client;
        _settings = settings;
        _logger   = logger;
    }

    /// <summary>Ubicación de registro/admisión para el identificador del paciente (fallback: LocationUuid).</summary>
    public string RegistrationLocationUuid =>
        string.IsNullOrEmpty(_settings.Defaults.RegistrationLocationUuid)
            ? _settings.Defaults.LocationUuid
            : _settings.Defaults.RegistrationLocationUuid;

    /// <summary>Asegura que existan los médicos de cada consultorio y arma los pares (ubicación, médico).</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var pares = new List<(string, string)>();
        foreach (var c in _settings.Defaults.Consultorios)
        {
            if (string.IsNullOrWhiteSpace(c.LocationUuid) || string.IsNullOrWhiteSpace(c.MedicoIdentifier))
                continue;

            var providerUuid = await EnsureMedicoAsync(c, ct);
            if (providerUuid is not null)
                pares.Add((c.LocationUuid, providerUuid));
        }

        _consultorios = pares;
        _logger.LogInformation("[Clinic] {N} consultorio(s) listos con médico asignado", _consultorios.Count);
    }

    /// <summary>Devuelve un par (ubicación, médico) aleatorio para una visita.</summary>
    public (string Location, string Provider) Assign() =>
        Pick(_consultorios, _settings.Defaults.LocationUuid, _settings.Defaults.ProviderUuid, _rng.Next);

    /// <summary>
    /// Selección pura (RNG inyectado) para ser testeable sin red. Si la lista está vacía, cae
    /// a la ubicación/proveedor por defecto.
    /// </summary>
    public static (string Location, string Provider) Pick(
        IReadOnlyList<(string Location, string Provider)> consultorios,
        string fallbackLocation, string fallbackProvider, Func<int, int> nextInt)
    {
        if (consultorios.Count == 0) return (fallbackLocation, fallbackProvider);
        return consultorios[nextInt(consultorios.Count)];
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    /// <summary>UUID del provider con ese identificador; lo crea (person + provider) si no existe.</summary>
    private async Task<string?> EnsureMedicoAsync(ConsultorioSettings c, CancellationToken ct)
    {
        try
        {
            var existing = await FindProviderUuidAsync(c.MedicoIdentifier, ct);
            if (existing is not null)
            {
                _logger.LogDebug("[Clinic] Médico {Id} ya existe ({Uuid})", c.MedicoIdentifier, existing);
                return existing;
            }

            var (given, family) = SplitNombre(c.MedicoNombre, c.MedicoIdentifier);

            var personJson = await _client.PostAsync("person", new
            {
                names  = new[] { new { givenName = given, familyName = family, preferred = true } },
                gender = "M"
            }, ct);
            var personUuid = JsonSerializer.Deserialize<JsonElement>(personJson).GetProperty("uuid").GetString();

            var providerJson = await _client.PostAsync("provider", new
            {
                person     = personUuid,
                identifier = c.MedicoIdentifier
            }, ct);
            var providerUuid = JsonSerializer.Deserialize<JsonElement>(providerJson).GetProperty("uuid").GetString();

            _logger.LogInformation("[Clinic] Médico creado {Nombre} ({Id}) → {Uuid}",
                c.MedicoNombre, c.MedicoIdentifier, providerUuid);
            return providerUuid;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Clinic] Error asegurando médico {Id}: {Msg}", c.MedicoIdentifier, ex.Message);
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

    private static (string Given, string Family) SplitNombre(string nombre, string fallback)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return (fallback, "Médico");
        var parts = nombre.Trim().Split(' ', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], "Médico");
    }
}
