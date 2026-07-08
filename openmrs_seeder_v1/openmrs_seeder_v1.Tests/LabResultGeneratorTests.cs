using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;
using static OpenmrsSeeder.Services.LabResultGenerator;

namespace openmrs_seeder_v1.Tests;

public class LabResultGeneratorTests
{
    private static readonly ISet<string> SinCategorias    = new HashSet<string>();
    private static readonly ISet<string> SinDx            = new HashSet<string>();

    private static LaboratorioEntry Numerico() => new()
    {
        CielUuid = "glucemia", Datatype = "numeric",
        ResMin = 70, ResMax = 99, ResMinAnormal = 126, ResMaxAnormal = 260,
        ResTrigger = ["diabetes", "endocrino"]
    };

    private static LaboratorioEntry Codificado() => new()
    {
        CielUuid = "ns1", Datatype = "coded",
        ResNormalUuid = "NEG", ResAnormalUuid = "POS",
        ResTriggerDx = ["dengue-uuid"]
    };

    // ── Numérico ──────────────────────────────────────────────────────────────

    [Fact]
    public void Numerico_SinTrigger_SiempreEnBandaNormal()
    {
        var lab = Numerico();
        var rng = new Random(1);
        for (int i = 0; i < 500; i++)
        {
            var r = Generar(lab, SinCategorias, SinDx, rng);
            Assert.Equal(TipoResultado.Numerico, r.Tipo);
            Assert.InRange(r.Numerico!.Value, lab.ResMin, lab.ResMax);
        }
    }

    [Fact]
    public void Numerico_ConTrigger_MayoriaAnormal()
    {
        var lab = Numerico();
        var cats = new HashSet<string> { "diabetes" };
        var rng = new Random(2);
        int anormales = 0;
        const int N = 2000;
        for (int i = 0; i < N; i++)
        {
            var v = Generar(lab, cats, SinDx, rng).Numerico!.Value;
            // El valor siempre cae en la banda normal O la anormal
            Assert.True((v >= lab.ResMin && v <= lab.ResMax) ||
                        (v >= lab.ResMinAnormal && v <= lab.ResMaxAnormal));
            if (v >= lab.ResMinAnormal) anormales++;
        }
        // ProbAnormalSiTrigger = 0.80 → esperamos ~80% (margen amplio)
        Assert.InRange(anormales / (double)N, 0.72, 0.88);
    }

    // ── Codificado ────────────────────────────────────────────────────────────

    [Fact]
    public void Codificado_SinTrigger_SiempreNormal()
    {
        var lab = Codificado();
        var rng = new Random(3);
        for (int i = 0; i < 500; i++)
        {
            var r = Generar(lab, SinCategorias, SinDx, rng);
            Assert.Equal(TipoResultado.Codificado, r.Tipo);
            Assert.Equal("NEG", r.CodedUuid);
        }
    }

    [Fact]
    public void Codificado_ConTriggerDx_MayoriaAnormal()
    {
        var lab = Codificado();
        var dx = new HashSet<string> { "dengue-uuid" };
        var rng = new Random(4);
        int positivos = 0;
        const int N = 2000;
        for (int i = 0; i < N; i++)
            if (Generar(lab, SinCategorias, dx, rng).CodedUuid == "POS") positivos++;
        Assert.InRange(positivos / (double)N, 0.72, 0.88);
    }

    [Fact]
    public void Codificado_SinRespuestaAnormal_DevuelveNinguno_CuandoAnormal()
    {
        // coded sin ResAnormalUuid: si se dispara anormal, no hay UUID → Ninguno
        var lab = new LaboratorioEntry
        {
            Datatype = "coded", ResNormalUuid = "NEG", ResAnormalUuid = "",
            ResTrigger = ["infeccioso"]
        };
        var cats = new HashSet<string> { "infeccioso" };
        var rng = new Random(5);
        // Sobre muchas iteraciones aparecerán ambos: Ninguno (anormal sin uuid) y Codificado (normal)
        var tipos = new HashSet<TipoResultado>();
        for (int i = 0; i < 200; i++) tipos.Add(Generar(lab, cats, SinDx, rng).Tipo);
        Assert.Contains(TipoResultado.Ninguno, tipos);
    }

    // ── Sin resultado (panel | imagen | vacío) ─────────────────────────────────

    [Theory]
    [InlineData("panel")]
    [InlineData("imagen")]
    [InlineData("")]
    [InlineData("otro")]
    public void DatatypeSinResultado_DevuelveNinguno(string datatype)
    {
        var lab = new LaboratorioEntry { Datatype = datatype };
        var r = Generar(lab, new HashSet<string> { "diabetes" }, new HashSet<string> { "x" }, new Random(6));
        Assert.Equal(TipoResultado.Ninguno, r.Tipo);
        Assert.Null(r.Numerico);
        Assert.Null(r.CodedUuid);
    }

    [Fact]
    public void Numerico_RangoInvertido_SeCorrige()
    {
        // Defensa: si min>max en el catálogo, no debe lanzar ni salir del rango
        var lab = new LaboratorioEntry { Datatype = "numeric", ResMin = 99, ResMax = 70 };
        var r = Generar(lab, SinCategorias, SinDx, new Random(7));
        Assert.InRange(r.Numerico!.Value, 70, 99);
    }
}
