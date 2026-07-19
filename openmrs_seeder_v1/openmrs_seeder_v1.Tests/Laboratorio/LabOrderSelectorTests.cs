using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Laboratorio;

/// <summary>
/// La selección de labs de la visita (seam extraído de LabOrderSeeder). Lo central: el examen
/// confirmatorio de un dx (índice inverso de res_trigger_dx) se ordena SIEMPRE — antes el dengue
/// podía irse sin su NS1 porque la selección era un sorteo por categoría.
/// </summary>
public class LabOrderSelectorTests
{
    private static readonly Dictionary<string, DateOnly> SinVigencias = [];
    private static readonly DateOnly Fecha = new(2024, 6, 10);

    private static LaboratorioEntry Lab(string uuid, bool infeccioso = false, bool urologico = false,
        bool chequeo = false, string datatype = "numeric", params string[] triggerDx) => new()
    {
        CielUuid = uuid, NombreEs = uuid, Datatype = datatype,
        AplicaInfeccioso = infeccioso, AplicaUrologico = urologico,
        EsChequeo = chequeo, ResTriggerDx = [.. triggerDx]
    };

    private static IReadOnlyDictionary<string, IReadOnlyList<LaboratorioEntry>> Indice(
        params LaboratorioEntry[] labs) =>
        labs.SelectMany(l => l.ResTriggerDx.Select(dx => (dx, l)))
            .GroupBy(x => x.dx)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LaboratorioEntry>)g.Select(x => x.l).ToList());

    // ── Confirmatorio garantizado ─────────────────────────────────────────────

    [Fact]
    public void DxConConfirmatorio_ElExamenSaleSiempre()
    {
        var ns1 = Lab("ns1", infeccioso: true, datatype: "coded", triggerDx: "dengue");
        var catalogo = new List<LaboratorioEntry> { ns1, Lab("pcr", infeccioso: true) };

        // 200 corridas con semillas distintas: el NS1 sale en TODAS (no depende de la tirada).
        for (int seed = 0; seed < 200; seed++)
        {
            var elegidos = LabOrderSelector.Seleccionar(
                catalogo, Indice(ns1), ["infeccioso"], ["dengue"],
                requiereLab: false, probBase: 0.40, SinVigencias, Fecha, new Random(seed));
            Assert.Contains(elegidos, l => l.CielUuid == "ns1");
        }
    }

    [Fact]
    public void ConfirmatorioConOrdenVigente_NoSeReordena()
    {
        var ns1 = Lab("ns1", infeccioso: true, datatype: "coded", triggerDx: "dengue");
        var vigencias = new Dictionary<string, DateOnly> { ["ns1"] = Fecha.AddDays(3) };

        var elegidos = LabOrderSelector.Seleccionar(
            [ns1], Indice(ns1), ["infeccioso"], ["dengue"],
            requiereLab: false, probBase: 0.0, vigencias, Fecha, Azar.Rng());

        // Sin confirmatorio pendiente y con probBase 0, no se ordena nada.
        Assert.Empty(elegidos);
    }

    [Fact]
    public void DosDxQueCompartenConfirmatorio_UnaSolaOrden()
    {
        var orina = Lab("orina", infeccioso: false, urologico: true, chequeo: false, "coded", "itu", "pielonefritis");

        var elegidos = LabOrderSelector.Seleccionar(
            [orina], Indice(orina), ["urologico"], ["itu", "pielonefritis"],
            requiereLab: false, probBase: 0.0, SinVigencias, Fecha, Azar.Rng());

        Assert.Single(elegidos);
    }

    [Fact]
    public void ConConfirmatorio_LosAcompanantesSonComoMaximoUno()
    {
        var ns1 = Lab("ns1", infeccioso: true, datatype: "coded", triggerDx: "dengue");
        var catalogo = new List<LaboratorioEntry>
            { ns1, Lab("pcr", infeccioso: true), Lab("hemograma", infeccioso: true) };

        for (int seed = 0; seed < 100; seed++)
        {
            var elegidos = LabOrderSelector.Seleccionar(
                catalogo, Indice(ns1), ["infeccioso"], ["dengue"],
                requiereLab: false, probBase: 0.40, SinVigencias, Fecha, new Random(seed));
            Assert.InRange(elegidos.Count, 1, 2); // el NS1 + 0-1 de categoría
        }
    }

    // ── Sin confirmatorio: comportamiento histórico ───────────────────────────

    [Fact]
    public void SinConfirmatorio_ProbBaseCeroYSinRequiereLab_NoOrdena()
    {
        var elegidos = LabOrderSelector.Seleccionar(
            [Lab("pcr", infeccioso: true)], Indice(), ["infeccioso"], ["gripe"],
            requiereLab: false, probBase: 0.0, SinVigencias, Fecha, Azar.Rng());
        Assert.Empty(elegidos);
    }

    [Fact]
    public void SinConfirmatorio_Ordena1o2LabsDeCategoria()
    {
        var catalogo = new List<LaboratorioEntry>
            { Lab("pcr", infeccioso: true), Lab("hemograma", infeccioso: true), Lab("bun", urologico: true) };

        int con2 = 0, total = 0;
        for (int seed = 0; seed < 500; seed++)
        {
            var elegidos = LabOrderSelector.Seleccionar(
                catalogo, Indice(), ["infeccioso"], ["gripe"],
                requiereLab: true, probBase: 0.40, SinVigencias, Fecha, new Random(seed));
            if (elegidos.Count == 0) continue; // la tirada del 0.80 no disparó
            total++;
            Assert.All(elegidos, l => Assert.True(l.AplicaInfeccioso)); // nunca el de urológico
            Assert.InRange(elegidos.Count, 1, 2);
            if (elegidos.Count == 2) con2++;
        }
        Assert.True(total > 300);                       // requiere_lab → ~80 % ordena
        Assert.InRange(con2 / (double)total, 0.30, 0.50); // ~40 % con segundo lab
    }

    // ── Chequeo ───────────────────────────────────────────────────────────────

    [Fact]
    public void SeleccionarChequeo_SoloDelPoolChequeoYEnBanda()
    {
        var catalogo = new List<LaboratorioEntry>
        {
            Lab("hemograma", chequeo: true), Lab("glucemia", chequeo: true),
            Lab("orina", chequeo: true), Lab("lipidico", chequeo: true),
            Lab("amilasa") // no chequeo
        };

        for (int seed = 0; seed < 100; seed++)
        {
            var elegidos = LabOrderSelector.SeleccionarChequeo(
                catalogo, SinVigencias, Fecha, minLabs: 2, maxLabs: 4, new Random(seed));
            Assert.InRange(elegidos.Count, 2, 4);
            Assert.All(elegidos, l => Assert.True(l.EsChequeo));
            Assert.Equal(elegidos.Count, elegidos.Select(l => l.CielUuid).Distinct().Count());
        }
    }

    [Fact]
    public void SeleccionarChequeo_PoolMenorQueElMinimo_DevuelveLoQueHay()
    {
        var elegidos = LabOrderSelector.SeleccionarChequeo(
            [Lab("hemograma", chequeo: true)], SinVigencias, Fecha, 2, 4, Azar.Rng());
        Assert.Single(elegidos);
    }

    [Fact]
    public void ExamenAPeticion_EvitaElegidosYVigentes()
    {
        var catalogo = new List<LaboratorioEntry>
            { Lab("hemograma", chequeo: true), Lab("glucemia", chequeo: true), Lab("orina", chequeo: true) };
        var vigencias = new Dictionary<string, DateOnly> { ["orina"] = Fecha.AddDays(2) };

        for (int seed = 0; seed < 50; seed++)
        {
            var lab = LabOrderSelector.ExamenAPeticion(
                catalogo, ["hemograma"], vigencias, Fecha, new Random(seed));
            Assert.NotNull(lab);
            Assert.Equal("glucemia", lab!.CielUuid); // único candidato libre
        }
    }

    [Fact]
    public void ExamenAPeticion_SinCandidatos_DevuelveNull()
    {
        var lab = LabOrderSelector.ExamenAPeticion(
            [Lab("hemograma", chequeo: true)], ["hemograma"], SinVigencias, Fecha, Azar.Rng());
        Assert.Null(lab);
    }
}
