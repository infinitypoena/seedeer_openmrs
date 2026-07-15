namespace openmrs_seeder_v1.Tests.TestSupport;

/// <summary>
/// Localiza los artefactos REALES del repo (catálogos, appsettings.example.json, querys) subiendo desde
/// la carpeta del binario de test. Es lo que permite los tests de humo sobre los CSV de verdad: una
/// errata en un catálogo del repo rompe la suite, no una corrida de horas.
/// </summary>
public static class Repo
{
    /// <summary>Raíz del repo: el primer ancestro que contiene <c>leyes_simulacion.md</c>.</summary>
    public static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "leyes_simulacion.md"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "No se encontró la raíz del repo (leyes_simulacion.md) subiendo desde " + AppContext.BaseDirectory);
    }

    /// <summary>Carpeta de catálogos del proyecto de producción.</summary>
    public static string Catalogos() =>
        Path.Combine(Raiz(), "openmrs_seeder_v1", "openmrs_seeder_v1", "catalogs");

    /// <summary>Ruta de un catálogo concreto (p.ej. "diagnosticos.csv").</summary>
    public static string Catalogo(string nombre) => Path.Combine(Catalogos(), nombre);

    public static string AppSettingsExample() =>
        Path.Combine(Raiz(), "openmrs_seeder_v1", "openmrs_seeder_v1", "appsettings.example.json");

    /// <summary>Ruta de un script SQL versionado (p.ej. "sp_fechas_auditoria.sql").</summary>
    public static string Query(string nombre) => Path.Combine(Raiz(), "querys", nombre);
}
