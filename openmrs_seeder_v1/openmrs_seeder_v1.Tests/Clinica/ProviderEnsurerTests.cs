using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Clinica;

/// <summary>
/// <c>ProviderEnsurer.SplitNombre</c> parte el nombre del catálogo (consultorios.csv /
/// personal_laboratorio.csv) en given/family para el <c>POST /person</c> del médico o técnico.
/// </summary>
public class ProviderEnsurerTests
{
    [Theory]
    [InlineData("Ana Martínez", "Ana", "Martínez")]
    [InlineData("Juan Carlos Pérez García", "Juan", "Carlos Pérez García")]   // solo parte en el 1er espacio
    [InlineData("  Ana Martínez  ", "Ana", "Martínez")]                       // recorta espacios
    public void ParteElNombreEnElPrimerEspacio(string nombre, string given, string family)
    {
        Assert.Equal((given, family), ProviderEnsurer.SplitNombre(nombre, "fallback"));
    }

    [Fact]
    public void UnSoloNombre_CompletaConProfesional()
    {
        Assert.Equal(("Ana", "Profesional"), ProviderEnsurer.SplitNombre("Ana", "fallback"));
    }

    [Fact]
    public void NombreVacio_UsaElFallback()
    {
        Assert.Equal(("SIM-MED-1", "Profesional"), ProviderEnsurer.SplitNombre("  ", "SIM-MED-1"));
    }
}
