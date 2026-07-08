using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

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
    private readonly SimulationSettings _simSettings;
    private readonly CatalogLoader _catalogs;
    private readonly ILogger<ClinicResourceAssigner> _logger;
    private readonly Random _rng = new();

    private List<(string Location, string Provider)> _consultorios = [];
    /// <summary>Médicos que atienden HOY (subconjunto de <c>_consultorios</c>, refrescado por día).</summary>
    private List<(string Location, string Provider)> _consultoriosActivos = [];
    /// <summary>Prob. de la corrida de que un recurrente vuelva con su médico de cabecera.</summary>
    private double _runCabeceraProb;

    public ClinicResourceAssigner(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        SimulationSettings simSettings,
        CatalogLoader catalogs,
        ILogger<ClinicResourceAssigner> logger)
    {
        _client      = client;
        _settings    = settings;
        _simSettings = simSettings;
        _catalogs    = catalogs;
        _logger      = logger;
    }

    /// <summary>Ubicación de registro/admisión para el identificador del paciente (fallback: LocationUuid).</summary>
    public string RegistrationLocationUuid =>
        string.IsNullOrEmpty(_settings.Defaults.RegistrationLocationUuid)
            ? _settings.Defaults.LocationUuid
            : _settings.Defaults.RegistrationLocationUuid;

    /// <summary>
    /// Asegura que existan los médicos de cada consultorio y arma los pares (ubicación, médico).
    /// Fail-fast: si algún médico del catálogo no se puede asegurar, lanza y aborta la corrida
    /// (antes de crear datos) para que no queden encuentros firmados por un proveedor inexistente.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var resueltos = new List<(Models.Catalogs.ConsultorioEntry Entry, string? ProviderUuid)>();
        foreach (var c in _catalogs.Consultorios)
        {
            if (string.IsNullOrWhiteSpace(c.LocationUuid) || string.IsNullOrWhiteSpace(c.MedicoIdentifier))
                continue;

            var providerUuid = await EnsureMedicoAsync(c, ct);
            resueltos.Add((c, providerUuid));
        }

        _consultorios = ResolvePool(resueltos);

        // Prob. de cabecera de la corrida: una vez, uniforme en [Min, Max], inclinada al "sí"
        var min = _simSettings.MedicoCabeceraProbMin;
        var max = Math.Max(min, _simSettings.MedicoCabeceraProbMax);
        _runCabeceraProb = min + _rng.NextDouble() * (max - min);

        _logger.LogInformation("[Clinic] {N} consultorio(s) listos | P(médico de cabecera) de la corrida: {P:P0}",
            _consultorios.Count, _runCabeceraProb);
    }

    /// <summary>
    /// Sortea los médicos que atienden HOY: un subconjunto de tamaño aleatorio en
    /// [<c>MinMedicosPorDia</c>, <c>MaxMedicosPorDia</c>] (recortado al pool). Se llama una vez por
    /// día desde el orquestador para modelar una clínica pequeña donde no siempre están todos.
    /// </summary>
    public void ActivarMedicosDelDia(Random rng)
    {
        _consultoriosActivos = SeleccionarActivos(
            _consultorios, _simSettings.MinMedicosPorDia, _simSettings.MaxMedicosPorDia, rng);

        if (_consultoriosActivos.Count > 0 && _consultoriosActivos.Count < _consultorios.Count)
            _logger.LogInformation("[Clinic] Médicos de hoy: {N} de {Total} consultorios activos",
                _consultoriosActivos.Count, _consultorios.Count);
    }

    /// <summary>
    /// Selección pura (RNG inyectado) del subconjunto de médicos activos de un día. Elige un tamaño
    /// aleatorio en [min, max] recortado a [1, pool.Count] y toma esa cantidad de médicos DISTINTOS.
    /// Pool vacío → lista vacía (modo proveedor único por defecto). Testeable sin red.
    /// </summary>
    public static List<(string Location, string Provider)> SeleccionarActivos(
        IReadOnlyList<(string Location, string Provider)> pool, int min, int max, Random rng)
    {
        if (pool.Count == 0) return [];
        var lo = Math.Clamp(min, 1, pool.Count);
        var hi = Math.Clamp(max, lo, pool.Count);
        var n  = rng.Next(lo, hi + 1);
        return pool.OrderBy(_ => rng.Next()).Take(n).ToList();
    }

    /// <summary>
    /// Asigna consultorio + médico a la visita y mantiene el médico de cabecera del paciente:
    /// los nuevos estrenan cabecera; los recurrentes vuelven a su cabecera con probabilidad
    /// <c>_runCabeceraProb</c> —siempre que ese médico esté disponible hoy—, o caen con otro médico.
    /// </summary>
    public void AssignVisit(SimulatedPatient patient)
    {
        var tieneCabecera = !string.IsNullOrEmpty(patient.CabeceraProviderUuid);
        var activos = _consultoriosActivos.Count > 0 ? _consultoriosActivos : _consultorios;
        var cabeceraDisponible = tieneCabecera &&
            activos.Any(c => c.Provider == patient.CabeceraProviderUuid);

        if (UsarCabecera(cabeceraDisponible, patient.EsNuevo, _rng.NextDouble(), _runCabeceraProb))
        {
            patient.AssignedLocationUuid = patient.CabeceraLocationUuid;
            patient.AssignedProviderUuid = patient.CabeceraProviderUuid;
            return;
        }

        var (loc, prov) = Assign();
        patient.AssignedLocationUuid = loc;
        patient.AssignedProviderUuid = prov;

        // El paciente fija su cabecera la primera vez que recibe una
        if (!tieneCabecera)
        {
            patient.CabeceraLocationUuid = loc;
            patient.CabeceraProviderUuid = prov;
        }
    }

    /// <summary>Decisión pura: ¿atender a este paciente con su médico de cabecera? (RNG inyectado, testeable).</summary>
    public static bool UsarCabecera(bool tieneCabecera, bool esNuevo, double roll, double runProb) =>
        tieneCabecera && !esNuevo && roll < runProb;

    /// <summary>Devuelve un par (ubicación, médico) aleatorio de entre los médicos activos hoy.</summary>
    public (string Location, string Provider) Assign() =>
        Pick(_consultoriosActivos.Count > 0 ? _consultoriosActivos : _consultorios,
             _settings.Defaults.LocationUuid, _settings.Defaults.ProviderUuid, _rng.Next);

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

    /// <summary>
    /// Construye el pool (ubicación, médico) a partir de los médicos resueltos. Fail-fast:
    /// si algún consultorio quedó sin proveedor (no se pudo crear/encontrar), lanza
    /// <see cref="InvalidOperationException"/> listando los identificadores. Lista vacía
    /// (sin catálogo) → pool vacío sin lanzar (modo proveedor único por defecto). Método puro/testeable.
    /// </summary>
    public static List<(string Location, string Provider)> ResolvePool(
        IReadOnlyList<(Models.Catalogs.ConsultorioEntry Entry, string? ProviderUuid)> resueltos)
    {
        var faltantes = resueltos
            .Where(r => string.IsNullOrEmpty(r.ProviderUuid))
            .Select(r => r.Entry.MedicoIdentifier)
            .ToList();

        if (faltantes.Count > 0)
            throw new InvalidOperationException(
                $"No se pudieron asegurar {faltantes.Count} médico(s) del catálogo consultorios.csv: " +
                $"{string.Join(", ", faltantes)}. Abortando la corrida (revisa el log para el error REST).");

        return resueltos.Select(r => (r.Entry.LocationUuid, r.ProviderUuid!)).ToList();
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    /// <summary>UUID del provider con ese identificador; lo crea (person + provider) si no existe.</summary>
    private async Task<string?> EnsureMedicoAsync(Models.Catalogs.ConsultorioEntry c, CancellationToken ct)
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
                gender = string.IsNullOrWhiteSpace(c.MedicoGenero) ? "M" : c.MedicoGenero
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
