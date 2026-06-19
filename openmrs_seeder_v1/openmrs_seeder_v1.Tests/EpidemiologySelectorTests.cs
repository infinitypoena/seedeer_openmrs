using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class EpidemiologySelectorTests
{
    private static (CatalogLoader catalogs, EpidemiologySelector selector) CreateSelector()
    {
        var catalogs = new CatalogLoader();

        // Carga manual de entradas de prueba
        var epidemiology = new List<EpidemiologyEntry>
        {
            new() { Categoria = "respiratorio",   GrupoEdad = "0-14",  Genero = "M",     Peso = 35 },
            new() { Categoria = "respiratorio",   GrupoEdad = "0-14",  Genero = "F",     Peso = 32 },
            new() { Categoria = "infeccioso",     GrupoEdad = "0-14",  Genero = "Ambos", Peso = 20 },
            new() { Categoria = "cardiovascular", GrupoEdad = "45-64", Genero = "M",     Peso = 28 },
            new() { Categoria = "cardiovascular", GrupoEdad = "45-64", Genero = "F",     Peso = 22 },
            new() { Categoria = "diabetes",       GrupoEdad = "45-64", Genero = "Ambos", Peso = 20 },
        };

        var diagnosticos = new List<DiagnosticoEntry>
        {
            new()
            {
                CielUuid  = "uuid-bronquitis", NombreEs = "Bronquitis aguda",
                Categoria = "respiratorio", Severidad = "leve",
                Aplica0_14 = true, PesoM = 10, PesoF = 10,
                RequiereLab = false, RequiereRx = true
            },
            new()
            {
                CielUuid  = "uuid-hta", NombreEs = "Hipertensión arterial",
                Categoria = "cardiovascular", Severidad = "moderado",
                Aplica45_64 = true, PesoM = 28, PesoF = 22,
                RequiereLab = true, RequiereRx = true
            },
        };

        catalogs.LoadFromLists(epidemiology, diagnosticos, [], [], [], [], []);

        var settings = new SimulationSettings { RandomSeed = 42 };
        var selector = new EpidemiologySelector(catalogs, settings);
        return (catalogs, selector);
    }

    [Fact]
    public void SelectCategoria_DevuelveCategoriasConocidas()
    {
        var (_, selector) = CreateSelector();
        var categoriasValidas = new[] { "respiratorio", "infeccioso", "cardiovascular", "diabetes" };

        for (int i = 0; i < 20; i++)
        {
            var cat = selector.SelectCategoria("0-14", "M");
            Assert.Contains(cat, categoriasValidas);
        }
    }

    [Fact]
    public void SelectCategoria_FallbackAInfecciosoCuandoNoHayEntradas()
    {
        var (_, selector) = CreateSelector();
        // "15-29" no tiene entradas en los datos de prueba
        var cat = selector.SelectCategoria("15-29", "M");
        Assert.Equal("infeccioso", cat);
    }

    [Fact]
    public void SelectDiagnostico_MatcheaCategoriaYGrupoEdad()
    {
        var (_, selector) = CreateSelector();
        var dx = selector.SelectDiagnostico("respiratorio", "0-14", "M");
        Assert.NotNull(dx);
        Assert.Equal("respiratorio", dx!.Categoria);
    }

    [Fact]
    public void SelectDiagnostico_NullCuandoNoHayCoincidencia()
    {
        var (_, selector) = CreateSelector();
        // "respiratorio" + "45-64" no tiene ningún dx marcado aplica_45_64=true
        var dx = selector.SelectDiagnostico("respiratorio", "45-64", "M");
        Assert.Null(dx);
    }

    [Fact]
    public void SelectDiagnostico_DevuelveDistribucionEstadistica()
    {
        var (_, selector) = CreateSelector();
        // Con suficientes muestras debe devolver el único dx disponible consistentemente
        for (int i = 0; i < 10; i++)
        {
            var dx = selector.SelectDiagnostico("cardiovascular", "45-64", "M");
            Assert.NotNull(dx);
            Assert.Equal("uuid-hta", dx!.CielUuid);
        }
    }
}
