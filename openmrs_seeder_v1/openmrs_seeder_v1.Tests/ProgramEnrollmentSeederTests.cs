using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Seeders;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class ProgramEnrollmentSeederTests
{
    private static readonly ISet<string> Vacio = new HashSet<string>();

    private static ProgramaEntry Vih() => new()
    {
        ProgramUuid = "hiv-program", Nombre = "HIV Program",
        TriggerDx = ["vih-uuid-1", "vih-uuid-2"], TriggerCategoria = []
    };

    private static ProgramaEntry Prenatal() => new()
    {
        ProgramUuid = "mch-program", Nombre = "Prenatal",
        TriggerDx = [], TriggerCategoria = ["ginecoobstetrico"]
    };

    private static List<ProgramaEntry> Catalogo() => [Vih(), Prenatal()];

    [Fact]
    public void DxQueDispara_SeleccionaPrograma()
    {
        var r = ProgramEnrollmentSeeder.SeleccionarProgramas(
            Catalogo(), new HashSet<string> { "vih-uuid-1" }, Vacio, Vacio);
        Assert.Single(r);
        Assert.Equal("hiv-program", r[0].ProgramUuid);
    }

    [Fact]
    public void DxQueNoDispara_NoSelecciona()
    {
        var r = ProgramEnrollmentSeeder.SeleccionarProgramas(
            Catalogo(), new HashSet<string> { "gripe-uuid" }, new HashSet<string> { "respiratorio" }, Vacio);
        Assert.Empty(r);
    }

    [Fact]
    public void CategoriaQueDispara_SeleccionaPrograma()
    {
        var r = ProgramEnrollmentSeeder.SeleccionarProgramas(
            Catalogo(), Vacio, new HashSet<string> { "ginecoobstetrico" }, Vacio);
        Assert.Single(r);
        Assert.Equal("mch-program", r[0].ProgramUuid);
    }

    [Fact]
    public void YaInscrito_SeExcluye()
    {
        var r = ProgramEnrollmentSeeder.SeleccionarProgramas(
            Catalogo(), new HashSet<string> { "vih-uuid-2" }, Vacio,
            new HashSet<string> { "hiv-program" });
        Assert.Empty(r);
    }

    [Fact]
    public void MultipleDisparo_DevuelveAmbos()
    {
        var r = ProgramEnrollmentSeeder.SeleccionarProgramas(
            Catalogo(), new HashSet<string> { "vih-uuid-1" },
            new HashSet<string> { "ginecoobstetrico" }, Vacio);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void CatalogoVacio_DevuelveVacio()
    {
        var r = ProgramEnrollmentSeeder.SeleccionarProgramas(
            [], new HashSet<string> { "vih-uuid-1" }, new HashSet<string> { "ginecoobstetrico" }, Vacio);
        Assert.Empty(r);
    }
}
