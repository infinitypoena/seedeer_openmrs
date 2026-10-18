using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Clinica;

public class DireccionBoostTests
{
    private static List<DireccionEntry> DireccionesTipo() =>
    [
        new() { Departamento = "San Salvador", Municipio = "San Salvador", Zona = "Colonia A", Peso = 20 },
        new() { Departamento = "San Salvador", Municipio = "Mejicanos",    Zona = "Colonia B", Peso = 8  },
        new() { Departamento = "La Libertad",  Municipio = "Santa Tecla",  Zona = "Colonia C", Peso = 5  },
        new() { Departamento = "Morazán",      Municipio = "Perquín",      Zona = "Barrio D",  Peso = 1  },
    ];

    private static DireccionesSettings Activo(double prob = 1.0, double share = 0.30) => new()
    {
        Enabled = true,
        ProbabilidadBoostDia = prob,
        Share = share
    };

    [Fact]
    public void Elegir_Deshabilitado_DevuelveNullYSinBoost()
    {
        var cfg = new DireccionesSettings { Enabled = false, ProbabilidadBoostDia = 1.0, Share = 0.30 };

        var (dep, share) = DireccionBoost.ElegirDepartamentoBoost(
            DireccionesTipo(), cfg, 42, new DateOnly(2026, 10, 5));

        Assert.Null(dep);
        Assert.Equal(0.0, share);
    }

    [Fact]
    public void Elegir_SinDirecciones_DevuelveNullYSinBoost()
    {
        var (dep, share) = DireccionBoost.ElegirDepartamentoBoost(
            [], Activo(), 42, new DateOnly(2026, 10, 5));

        Assert.Null(dep);
        Assert.Equal(0.0, share);
    }

    [Fact]
    public void Elegir_ProbabilidadCero_NuncaHayBoost()
    {
        var inicio = new DateOnly(2026, 10, 1);
        for (var i = 0; i < 40; i++)
        {
            var (dep, _) = DireccionBoost.ElegirDepartamentoBoost(
                DireccionesTipo(), Activo(prob: 0.0), 42, inicio.AddDays(i));
            Assert.Null(dep);
        }
    }

    [Fact]
    public void Elegir_ProbabilidadUno_SiempreHayBoostYNuncaEsElDominante()
    {
        var inicio = new DateOnly(2026, 10, 1);
        for (var i = 0; i < 40; i++)
        {
            var (dep, share) = DireccionBoost.ElegirDepartamentoBoost(
                DireccionesTipo(), Activo(prob: 1.0, share: 0.30), 42, inicio.AddDays(i));

            Assert.NotNull(dep);
            Assert.NotEqual("San Salvador", dep);
            Assert.Equal(0.30, share);
        }
    }

    [Fact]
    public void Elegir_Determinista_MismaSemillaYFechaMismoResultado()
    {
        var fecha = new DateOnly(2026, 10, 5);

        var a = DireccionBoost.ElegirDepartamentoBoost(DireccionesTipo(), Activo(), 42, fecha);
        var b = DireccionBoost.ElegirDepartamentoBoost(DireccionesTipo(), Activo(), 42, fecha);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Elegir_SoloUnDepartamentoDominante_SinElegibles_DevuelveNull()
    {
        var solo = new List<DireccionEntry>
        {
            new() { Departamento = "San Salvador", Municipio = "San Salvador", Zona = "X", Peso = 10 }
        };

        var (dep, _) = DireccionBoost.ElegirDepartamentoBoost(solo, Activo(), 42, new DateOnly(2026, 10, 5));

        Assert.Null(dep);
    }

    [Fact]
    public void MultiplicadorBoost_DepartamentoChicoAlcanzaLaMismaCuota()
    {
        // Con share 0.30, un departamento de peso 1 capta el 30 % del día igual que uno grande.
        var m = DireccionBoost.MultiplicadorBoost(DireccionesTipo(), "Morazán", 0.30);

        // pesoBoost=1, pesoOtros=33 → m = 0.30·33 / (1·0.70) = 14.142…
        Assert.InRange(m, 14.0, 14.3);
        // Verificación de la cuota efectiva: P·m / (P·m + resto)
        var cuota = (1.0 * m) / (1.0 * m + 33.0);
        Assert.InRange(cuota, 0.299, 0.301);
    }

    [Fact]
    public void MultiplicadorBoost_SinBoost_DevuelveUno()
    {
        Assert.Equal(1.0, DireccionBoost.MultiplicadorBoost(DireccionesTipo(), null, 0.30));
        Assert.Equal(1.0, DireccionBoost.MultiplicadorBoost(DireccionesTipo(), "Morazán", 0.0));
        Assert.Equal(1.0, DireccionBoost.MultiplicadorBoost(DireccionesTipo(), "Morazán", 1.0));
    }
}
