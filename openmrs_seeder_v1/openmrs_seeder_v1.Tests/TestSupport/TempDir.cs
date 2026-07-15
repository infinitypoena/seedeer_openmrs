namespace openmrs_seeder_v1.Tests.TestSupport;

/// <summary>
/// Carpeta temporal desechable para los tests que necesitan ficheros reales (CatalogLoader lee de disco,
/// RunReportWriter escribe CSVs). Se borra sola al salir del <c>using</c>.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Ruta { get; } =
        Path.Combine(Path.GetTempPath(), "seeder-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Ruta);

    /// <summary>Escribe un fichero (p.ej. un CSV sintético) y devuelve su ruta completa.</summary>
    public string Escribir(string nombre, string contenido)
    {
        var ruta = Path.Combine(Ruta, nombre);
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    public void Dispose()
    {
        try { Directory.Delete(Ruta, recursive: true); } catch { /* best-effort */ }
    }
}
