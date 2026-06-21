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
}
