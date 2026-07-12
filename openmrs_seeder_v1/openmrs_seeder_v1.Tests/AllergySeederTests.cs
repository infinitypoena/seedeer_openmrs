using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Seeders;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class AllergySeederTests
{
    private static AllergySettings Settings(
        double second = 0.30, double third = 0.25, int max = 3) =>
        new()
        {
            SecondAllergyProbability = second,
            ThirdAllergyProbability  = third,
            MaxAllergies             = max
        };

    /// <summary>Cola de rolls fijos para simular el RNG de forma determinista.</summary>
    private static Func<double> Rolls(params double[] valores)
    {
        var i = 0;
        return () => valores[i++];
    }

    [Fact]
    public void Decaida_PrimerRollAlto_DaUnaSolaAlergia()
    {
        // 0.99 >= SecondAllergyProbability → se queda en 1
        var n = AllergySeeder.DecidirCantidad(10, Settings(), Rolls(0.99));
        Assert.Equal(1, n);
    }

    [Fact]
    public void Decaida_SegundoSiTercerNo_DaDos()
    {
        // 1er roll pasa (2ª), 2do roll no pasa (3ª)
        var n = AllergySeeder.DecidirCantidad(10, Settings(), Rolls(0.10, 0.99));
        Assert.Equal(2, n);
    }

    [Fact]
    public void Decaida_AmbosRollsPasan_DaTres()
    {
        var n = AllergySeeder.DecidirCantidad(10, Settings(), Rolls(0.10, 0.10));
        Assert.Equal(3, n);
    }

    [Fact]
    public void SecondProbabilityCero_SiempreUna()
    {
        var n = AllergySeeder.DecidirCantidad(10, Settings(second: 0.0), Rolls(0.0001, 0.0001));
        Assert.Equal(1, n);
    }

    [Fact]
    public void Acotado_PorTamanoDelCatalogo()
    {
        // Aunque ambos rolls pasen (querría 3), el pool solo tiene 2 alérgenos
        var n = AllergySeeder.DecidirCantidad(2, Settings(), Rolls(0.10, 0.10));
        Assert.Equal(2, n);
    }

    [Fact]
    public void Acotado_PorMaxAllergies()
    {
        var n = AllergySeeder.DecidirCantidad(10, Settings(max: 2), Rolls(0.10, 0.10));
        Assert.Equal(2, n);
    }

    [Fact]
    public void CatalogoVacio_DaCero()
    {
        var n = AllergySeeder.DecidirCantidad(0, Settings(), Rolls());
        Assert.Equal(0, n);
    }
}
