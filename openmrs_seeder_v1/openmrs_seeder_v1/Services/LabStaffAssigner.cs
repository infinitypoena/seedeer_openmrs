using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>Un profesional del laboratorio ya resuelto contra OpenMRS.</summary>
public readonly record struct LabStaff(string ProviderUuid, string Nombre, string Identifier, bool EsResponsable);

/// <summary>
/// Personal del laboratorio: quién toma la muestra (técnico) y quién valida el resultado (responsable).
/// Hermano de <see cref="ClinicResourceAssigner"/> — mismo patrón: al inicio de la corrida asegura de
/// forma idempotente que cada persona de <c>personal_laboratorio.csv</c> exista como provider (vía el
/// <see cref="ProviderEnsurer"/> compartido) y luego reparte por orden.
/// <para>
/// <b>Fail-fast</b>: si alguien del catálogo no se puede asegurar, lanza y la corrida aborta antes de
/// crear datos (nada de encuentros de laboratorio firmados por un provider inexistente).
/// <b>Catálogo vacío/ausente</b> = feature apagada: <see cref="Activo"/> es false y el flujo cae al
/// comportamiento histórico (el médico de la consulta firma el resultado).
/// </para>
/// </summary>
public class LabStaffAssigner
{
    private readonly ProviderEnsurer _providers;
    private readonly CatalogLoader _catalogs;
    private readonly ILogger<LabStaffAssigner> _logger;
    private readonly Random _rng;

    private List<LabStaff> _tecnicos = [];
    private LabStaff? _responsable;

    public LabStaffAssigner(
        ProviderEnsurer providers,
        CatalogLoader catalogs,
        SimulationSettings simSettings,
        ILogger<LabStaffAssigner> logger)
    {
        _providers = providers;
        _catalogs  = catalogs;
        _logger    = logger;
        _rng       = new Random(simSettings.RandomSeed + 22);
    }

    /// <summary>Hay personal de laboratorio operativo (catálogo con al menos un técnico).</summary>
    public bool Activo => _tecnicos.Count > 0;

    /// <summary>
    /// Asegura (idempotente) que todo el personal del catálogo exista en OpenMRS. Fail-fast si alguno
    /// falla. Sin catálogo no lanza: simplemente la feature queda apagada.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var resueltos = new List<(PersonalLaboratorioEntry Entry, string? ProviderUuid)>();
        foreach (var p in _catalogs.PersonalLaboratorio)
        {
            if (string.IsNullOrWhiteSpace(p.Identifier)) continue;
            var uuid = await _providers.EnsureAsync(p.Identifier, p.Nombre, p.Genero, ct);
            resueltos.Add((p, uuid));
        }

        var pool = ResolvePool(resueltos);
        _tecnicos    = pool.Where(s => !s.EsResponsable).ToList();
        _responsable = pool.FirstOrDefault(s => s.EsResponsable);

        // Sin responsable explícito, valida el propio técnico (una clínica pequeña puede no tener jefatura).
        if (_tecnicos.Count == 0 && pool.Count > 0)
            _tecnicos = pool;

        if (Activo)
            _logger.LogInformation("[Lab] Personal de laboratorio listo: {Tecnicos} técnico(s){Resp}",
                _tecnicos.Count,
                _responsable is { } r ? $" + responsable {r.Nombre}" : " (sin responsable)");
    }

    /// <summary>
    /// Construye el pool desde los resueltos. Fail-fast: si alguien quedó sin provider, lanza listando
    /// los identificadores. Lista vacía (sin catálogo) → pool vacío sin lanzar. Método puro/testeable.
    /// </summary>
    public static List<LabStaff> ResolvePool(
        IReadOnlyList<(PersonalLaboratorioEntry Entry, string? ProviderUuid)> resueltos)
    {
        var faltantes = resueltos
            .Where(r => string.IsNullOrEmpty(r.ProviderUuid))
            .Select(r => r.Entry.Identifier)
            .ToList();

        if (faltantes.Count > 0)
            throw new InvalidOperationException(
                $"No se pudo asegurar {faltantes.Count} persona(s) de personal_laboratorio.csv: " +
                $"{string.Join(", ", faltantes)}. Abortando la corrida (revisa el log para el error REST).");

        return resueltos
            .Select(r => new LabStaff(r.ProviderUuid!, r.Entry.Nombre, r.Entry.Identifier, r.Entry.EsResponsable))
            .ToList();
    }

    /// <summary>Técnico que toma la muestra y registra el resultado. <c>null</c> = feature apagada.</summary>
    public LabStaff? Tecnico() => _tecnicos.Count == 0 ? null : _tecnicos[_rng.Next(_tecnicos.Count)];

    /// <summary>
    /// Quien valida y cierra el resultado: el responsable del laboratorio si existe; si no, el técnico
    /// que lo procesó (en una clínica pequeña el mismo técnico valida su corrida).
    /// </summary>
    public LabStaff? Validador(LabStaff? tecnico) => _responsable ?? tecnico;
}
