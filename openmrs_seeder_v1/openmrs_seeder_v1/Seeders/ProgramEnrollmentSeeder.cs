using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

/// <summary>
/// Inscribe al paciente en los programas de atención de OpenMRS (<c>POST /programenrollment</c>)
/// cuando alguno de sus diagnósticos (por UUID) o categorías coincide con el disparo de
/// <c>programas.csv</c>. Deduplicado por paciente (<see cref="SimulatedPatient.EnrolledPrograms"/>)
/// → no se reinscribe entre visitas recurrentes. Catálogo vacío/ausente = inactivo.
/// </summary>
public class ProgramEnrollmentSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly ILogger<ProgramEnrollmentSeeder> _logger;

    public ProgramEnrollmentSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        ILogger<ProgramEnrollmentSeeder> logger)
    {
        _client   = client;
        _settings = settings;
        _catalogs = catalogs;
        _logger   = logger;
    }

    /// <summary>
    /// Seam puro y testeable (sin red): programas cuyo disparo (dx o categoría) coincide y que el
    /// paciente aún no tiene inscritos.
    /// </summary>
    public static List<ProgramaEntry> SeleccionarProgramas(
        IEnumerable<ProgramaEntry> catalogo,
        ISet<string> dxUuids,
        ISet<string> categorias,
        ISet<string> yaInscritos) =>
        catalogo
            .Where(p => !yaInscritos.Contains(p.ProgramUuid))
            .Where(p => p.TriggerDx.Any(dxUuids.Contains) || p.TriggerCategoria.Any(categorias.Contains))
            .ToList();

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (_catalogs.Programas.Count == 0) return;

        var dxUuids    = patient.TodosDiagnosticos.Select(d => d.CielUuid).ToHashSet();
        var categorias = patient.Categorias.ToHashSet();

        var elegibles = SeleccionarProgramas(_catalogs.Programas, dxUuids, categorias, patient.EnrolledPrograms);

        foreach (var prog in elegibles)
        {
            // Dedupe definitivo: marca inscrito (compartido con las visitas recurrentes por referencia).
            if (!patient.EnrolledPrograms.Add(prog.ProgramUuid)) continue;

            var fecha = VisitSeeder.FormatDatetime(patient.VisitDatetime);
            var payload = new
            {
                patient      = patient.OpenMrsUuid,
                program      = prog.ProgramUuid,
                dateEnrolled = fecha,
                location     = patient.AssignedLocationUuid ?? _settings.Defaults.LocationUuid,
                states       = string.IsNullOrWhiteSpace(prog.EstadoInicialUuid)
                    ? null
                    : new[] { new { state = prog.EstadoInicialUuid, startDate = fecha } }
            };

            try
            {
                await _client.PostAsync("programenrollment", payload, ct);
                _logger.LogInformation("[Program] {Prog} inscrito para {Id}", prog.Nombre, patient.Identifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Program] Error inscribiendo {Prog} para {Id}: {Msg}",
                    prog.Nombre, patient.Identifier, ex.Message);
            }
        }
    }
}
