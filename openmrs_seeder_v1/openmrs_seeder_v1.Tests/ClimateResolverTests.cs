using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class ClimateResolverTests
{
    private static ClimateResolver Crear(bool enabled, params ClimaEntry[] clima)
    {
        var catalogs = new CatalogLoader();
        catalogs.LoadFromLists([], [], [], [], [], [], [], clima);
        var settings = new SimulationSettings { Climate = new ClimateSettings { Enabled = enabled } };
        return new ClimateResolver(catalogs, settings);
    }

    [Fact]
    public void Resolve_DevuelveEstacionYTemp_SegunSemanaISO()
    {
        var resolver = Crear(true,
            new ClimaEntry { Semana = 1,  Estacion = "invierno", TempPromedioC = 14.0 },
            new ClimaEntry { Semana = 23, Estacion = "verano",   TempPromedioC = 30.0 });

        // 2023-01-02 (lunes) = semana ISO 1
        var (e1, t1) = resolver.Resolve(new DateOnly(2023, 1, 2));
        Assert.Equal("invierno", e1);
        Assert.Equal(14.0, t1);

        // 2023-06-05 (lunes) = semana ISO 23
        var (e2, t2) = resolver.Resolve(new DateOnly(2023, 6, 5));
        Assert.Equal("verano", e2);
        Assert.Equal(30.0, t2);
    }

    [Fact]
    public void Resolve_SemanaNoListada_DevuelveNull()
    {
        var resolver = Crear(true,
            new ClimaEntry { Semana = 1, Estacion = "invierno", TempPromedioC = 14.0 });

        // 2023-06-05 = semana 23, no está en el catálogo
        var (e, t) = resolver.Resolve(new DateOnly(2023, 6, 5));
        Assert.Null(e);
        Assert.Null(t);
    }

    [Fact]
    public void Resolve_Deshabilitado_DevuelveNull()
    {
        var resolver = Crear(false,
            new ClimaEntry { Semana = 1, Estacion = "invierno", TempPromedioC = 14.0 });

        Assert.False(resolver.Activo);
        var (e, t) = resolver.Resolve(new DateOnly(2023, 1, 2));
        Assert.Null(e);
        Assert.Null(t);
    }

    [Fact]
    public void Resolve_SinCatalogo_DevuelveNull()
    {
        var resolver = Crear(true); // sin filas de clima
        Assert.False(resolver.Activo);
        var (e, _) = resolver.Resolve(new DateOnly(2023, 1, 2));
        Assert.Null(e);
    }
}
