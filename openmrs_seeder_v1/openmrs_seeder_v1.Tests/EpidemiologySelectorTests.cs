using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class EpidemiologySelectorTests
{
    private static (CatalogLoader catalogs, EpidemiologySelector selector) CreateSelector(
        SimulationSettings? settings = null)
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
            new() { Categoria = "osteomuscular",  GrupoEdad = "45-64", Genero = "Ambos", Peso = 18 },
        };

        var diagnosticos = new List<DiagnosticoEntry>
        {
            new()
            {
                CielUuid  = "uuid-bronquitis", NombreEs = "Bronquitis aguda",
                Categoria = "respiratorio", Severidad = "leve",
                Aplica0_14 = true, PesoM = 10, PesoF = 10,
                RequiereLab = false, RequiereRx = true, EsComun = true
            },
            new()
            {
                CielUuid  = "uuid-gripe", NombreEs = "Influenza (gripe)",
                Categoria = "respiratorio", Severidad = "leve",
                Aplica0_14 = true, PesoM = 10, PesoF = 10,
                RequiereLab = false, RequiereRx = true,
                Clima = ["invierno"], EsComun = false
            },
            new()
            {
                CielUuid  = "uuid-hta", NombreEs = "Hipertensión arterial",
                Categoria = "cardiovascular", Severidad = "moderado",
                Aplica45_64 = true, PesoM = 28, PesoF = 22,
                RequiereLab = true, RequiereRx = true
            },
            new()
            {
                CielUuid  = "uuid-dm2", NombreEs = "Diabetes mellitus tipo 2",
                Categoria = "diabetes", Severidad = "moderado",
                Aplica45_64 = true, PesoM = 20, PesoF = 20,
                RequiereLab = true, RequiereRx = true
            },
            new()
            {
                CielUuid  = "uuid-lumbalgia", NombreEs = "Lumbalgia",
                Categoria = "osteomuscular", Severidad = "leve",
                Aplica45_64 = true, PesoM = 18, PesoF = 18,
                RequiereLab = false, RequiereRx = true
            },
        };

        var afinidades = new List<AfinidadEntry>
        {
            new() { Categoria = "diabetes",       Afines = ["cardiovascular", "endocrino"] },
            new() { Categoria = "cardiovascular", Afines = ["diabetes", "endocrino"] },
        };

        catalogs.LoadFromLists(epidemiology, diagnosticos, [], [], [], [], [], afinidades: afinidades);

        settings ??= new SimulationSettings { RandomSeed = 42 };
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

    [Fact]
    public void SelectComorbilidades_ConProbabilidadCero_DevuelveVacio()
    {
        var settings = new SimulationSettings
        {
            RandomSeed = 42,
            Comorbidity = new ComorbiditySettings { BaseProbability = 0.0 }
        };
        var (_, selector) = CreateSelector(settings);
        var primario = selector.SelectDiagnostico("diabetes", "45-64", "M");
        Assert.NotNull(primario);

        for (int i = 0; i < 20; i++)
            Assert.Empty(selector.SelectComorbilidades(primario!, "45-64", "M"));
    }

    [Fact]
    public void SelectComorbilidades_RespetaAfinidad_EligeCategoriaAsociada()
    {
        var settings = new SimulationSettings
        {
            RandomSeed = 42,
            Comorbidity = new ComorbiditySettings
            {
                BaseProbability = 1.0,
                MaxAdditional = 1,
                AffinityBoost = 50.0
            }
        };
        var (_, selector) = CreateSelector(settings);
        var primario = selector.SelectDiagnostico("diabetes", "45-64", "M");
        Assert.NotNull(primario);

        int conComorbilidad = 0, afines = 0;
        for (int i = 0; i < 200; i++)
        {
            var comorb = selector.SelectComorbilidades(primario!, "45-64", "M");
            foreach (var dx in comorb)
            {
                conComorbilidad++;
                // Nunca duplica la categoría del primario
                Assert.NotEqual("diabetes", dx.Categoria);
                // cardiovascular es afín a diabetes en los settings por defecto
                if (dx.Categoria == "cardiovascular") afines++;
            }
        }

        Assert.True(conComorbilidad > 0, "Debería generar comorbilidades con probabilidad 1.0");
        // Con un boost de afinidad alto, la mayoría deben ser de la categoría afín (cardiovascular)
        // frente a la alternativa no afín disponible (osteomuscular).
        Assert.True(afines > conComorbilidad * 0.7,
            $"Esperaba mayoría de comorbilidades afines; afines={afines}, total={conComorbilidad}");
    }

    [Fact]
    public void SelectDiagnostico_ConClima_FavoreceEnfermedadDeLaEstacion()
    {
        // gripe (clima=invierno) y bronquitis (sin clima) tienen el mismo peso base
        var (_, selector) = CreateSelector();

        int gripe = 0, bronquitis = 0;
        for (int i = 0; i < 400; i++)
        {
            var dx = selector.SelectDiagnostico("respiratorio", "0-14", "M", climate: "invierno");
            if (dx?.CielUuid == "uuid-gripe") gripe++;
            else if (dx?.CielUuid == "uuid-bronquitis") bronquitis++;
        }

        // Con SeasonalBoost (2.5) por defecto, la gripe debe dominar claramente en invierno
        Assert.True(gripe > bronquitis * 1.5,
            $"En invierno la gripe debería dominar; gripe={gripe}, bronquitis={bronquitis}");
    }

    [Fact]
    public void SelectDiagnostico_PreferCommon_FiltraPorPoolComun()
    {
        var (_, selector) = CreateSelector();

        // En respiratorio: bronquitis (comun) y gripe (no comun)
        for (int i = 0; i < 50; i++)
        {
            var comun = selector.SelectDiagnostico("respiratorio", "0-14", "M", preferCommon: true);
            Assert.Equal("uuid-bronquitis", comun!.CielUuid);

            var raro = selector.SelectDiagnostico("respiratorio", "0-14", "M", preferCommon: false);
            Assert.Equal("uuid-gripe", raro!.CielUuid);
        }
    }

    [Fact]
    public void DrawRunCommonProbability_DentroDeLaBanda_YVariaEntreCorridas()
    {
        var settings = new SimulationSettings { RandomSeed = 42, CommonProbMin = 0.70, CommonProbMax = 0.95 };
        var (_, selector) = CreateSelector(settings);

        var valores = new System.Collections.Generic.HashSet<double>();
        for (int i = 0; i < 50; i++)
        {
            var p = selector.DrawRunCommonProbability();
            Assert.InRange(p, 0.70, 0.95);   // siempre dentro de la banda (inclinado a común)
            valores.Add(p);
        }
        Assert.True(valores.Count > 1, "La probabilidad de la corrida debe variar");
    }

    [Fact]
    public void RollPreferCommon_RespetaLaProbabilidad()
    {
        var (_, selector) = CreateSelector();
        int comunes = 0;
        const int n = 2000;
        for (int i = 0; i < n; i++) if (selector.RollPreferCommon(0.80)) comunes++;
        Assert.InRange(comunes, (int)(n * 0.74), (int)(n * 0.86)); // ~80%
    }

    [Fact]
    public void SelectDiagnostico_SinClima_NoAplicaBoost()
    {
        var (_, selector) = CreateSelector();

        int gripe = 0, bronquitis = 0;
        for (int i = 0; i < 400; i++)
        {
            var dx = selector.SelectDiagnostico("respiratorio", "0-14", "M"); // climate = null
            if (dx?.CielUuid == "uuid-gripe") gripe++;
            else if (dx?.CielUuid == "uuid-bronquitis") bronquitis++;
        }

        // Sin clima, pesos iguales → reparto aproximadamente parejo (ninguno domina abrumadoramente)
        Assert.True(gripe > 0 && bronquitis > 0);
        Assert.True(gripe < bronquitis * 2 && bronquitis < gripe * 2,
            $"Sin clima el reparto debería ser parejo; gripe={gripe}, bronquitis={bronquitis}");
    }
}
