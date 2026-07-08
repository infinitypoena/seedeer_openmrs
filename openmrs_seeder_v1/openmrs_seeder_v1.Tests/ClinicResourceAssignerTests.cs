using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class ClinicResourceAssignerTests
{
    private static readonly List<(string Location, string Provider)> Consultorios =
    [
        ("loc-1", "med-1"),
        ("loc-2", "med-2"),
        ("loc-3", "med-3"),
        ("loc-4", "med-4"),
    ];

    [Fact]
    public void Pick_DevuelveElConsultorioIndicadoPorElRng()
    {
        var (loc, prov) = ClinicResourceAssigner.Pick(Consultorios, "fb-loc", "fb-prov", _ => 2);
        Assert.Equal("loc-3", loc);
        Assert.Equal("med-3", prov);
    }

    [Fact]
    public void Pick_ListaVacia_CaeAlFallback()
    {
        var (loc, prov) = ClinicResourceAssigner.Pick([], "fb-loc", "fb-prov", _ => 0);
        Assert.Equal("fb-loc", loc);
        Assert.Equal("fb-prov", prov);
    }

    [Fact]
    public void Pick_RngRecibeElTamanoDeLaLista()
    {
        int capturado = -1;
        ClinicResourceAssigner.Pick(Consultorios, "fb-loc", "fb-prov", n => { capturado = n; return 0; });
        Assert.Equal(Consultorios.Count, capturado);
    }

    [Fact]
    public void Pick_CubreTodosLosConsultorios()
    {
        for (int i = 0; i < Consultorios.Count; i++)
        {
            var idx = i;
            var (loc, prov) = ClinicResourceAssigner.Pick(Consultorios, "fb-loc", "fb-prov", _ => idx);
            Assert.Equal(Consultorios[idx].Location, loc);
            Assert.Equal(Consultorios[idx].Provider, prov);
        }
    }

    // ── Médico de cabecera ────────────────────────────────────────────────────

    [Fact]
    public void UsarCabecera_NuevoSiempreEstrena()
    {
        // Paciente nuevo: nunca usa cabecera (la estrena en esta visita), aunque el roll sea bajo
        Assert.False(ClinicResourceAssigner.UsarCabecera(tieneCabecera: false, esNuevo: true, roll: 0.0, runProb: 0.9));
    }

    [Fact]
    public void UsarCabecera_RecurrenteConRollBajo_VuelveASuCabecera()
    {
        Assert.True(ClinicResourceAssigner.UsarCabecera(tieneCabecera: true, esNuevo: false, roll: 0.10, runProb: 0.80));
    }

    [Fact]
    public void UsarCabecera_RecurrenteConRollAlto_CaeConOtroMedico()
    {
        Assert.False(ClinicResourceAssigner.UsarCabecera(tieneCabecera: true, esNuevo: false, roll: 0.95, runProb: 0.80));
    }

    [Fact]
    public void UsarCabecera_RecurrenteSinCabecera_NoLaUsa()
    {
        Assert.False(ClinicResourceAssigner.UsarCabecera(tieneCabecera: false, esNuevo: false, roll: 0.0, runProb: 0.90));
    }

    // ── SeleccionarActivos (roster diario de médicos) ─────────────────────────

    [Fact]
    public void SeleccionarActivos_TamanoDentroDelRango()
    {
        var rng = new Random(123);
        for (int i = 0; i < 200; i++)
        {
            var activos = ClinicResourceAssigner.SeleccionarActivos(Consultorios, 2, 3, rng);
            Assert.InRange(activos.Count, 2, 3);
        }
    }

    [Fact]
    public void SeleccionarActivos_MinMayorQuePool_SeRecortaAlPool()
    {
        var pool = Consultorios.Take(3).ToList();
        var activos = ClinicResourceAssigner.SeleccionarActivos(pool, 5, 9, new Random(1));
        Assert.Equal(3, activos.Count);
    }

    [Fact]
    public void SeleccionarActivos_PoolVacio_DevuelveVacio()
    {
        var activos = ClinicResourceAssigner.SeleccionarActivos([], 2, 3, new Random(1));
        Assert.Empty(activos);
    }

    [Fact]
    public void SeleccionarActivos_MedicosDistintos_SinRepetidos()
    {
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var activos = ClinicResourceAssigner.SeleccionarActivos(Consultorios, 2, 4, rng);
            Assert.Equal(activos.Count, activos.Select(a => a.Provider).Distinct().Count());
        }
    }

    [Fact]
    public void SeleccionarActivos_ElementosPertenecenAlPool()
    {
        var activos = ClinicResourceAssigner.SeleccionarActivos(Consultorios, 2, 3, new Random(7));
        Assert.All(activos, a => Assert.Contains(a, Consultorios));
    }

    // ── ResolvePool (fail-fast) ───────────────────────────────────────────────

    private static ConsultorioEntry Entry(string loc, string id) =>
        new() { LocationUuid = loc, MedicoIdentifier = id, MedicoNombre = id };

    [Fact]
    public void ResolvePool_TodosResueltos_DevuelvePares()
    {
        var pool = ClinicResourceAssigner.ResolvePool(
        [
            (Entry("loc-1", "MED-1"), "prov-1"),
            (Entry("loc-2", "MED-2"), "prov-2"),
        ]);

        Assert.Equal(2, pool.Count);
        Assert.Equal(("loc-1", "prov-1"), pool[0]);
        Assert.Equal(("loc-2", "prov-2"), pool[1]);
    }

    [Fact]
    public void ResolvePool_ConMedicoNoAsegurado_Lanza()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ClinicResourceAssigner.ResolvePool(
            [
                (Entry("loc-1", "MED-1"), "prov-1"),
                (Entry("loc-2", "MED-2"), null),
            ]));

        Assert.Contains("MED-2", ex.Message);
    }

    [Fact]
    public void ResolvePool_ListaVacia_DevuelveVacioSinLanzar()
    {
        var pool = ClinicResourceAssigner.ResolvePool([]);
        Assert.Empty(pool);
    }
}
