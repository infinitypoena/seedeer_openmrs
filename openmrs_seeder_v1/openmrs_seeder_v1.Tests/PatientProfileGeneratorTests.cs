using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class PatientProfileGeneratorTests
{
    private static PatientProfileGenerator CreateGen(int seed = 42) =>
        new(new SimulationSettings { RandomSeed = seed });

    [Fact]
    public void GenerateNew_IdentificadorConPrefixSIM()
    {
        var gen = CreateGen();
        var p   = gen.GenerateNew();
        Assert.StartsWith("SIM-", p.Identifier);
        Assert.Equal(12, p.Identifier.Length); // "SIM-" + 8 hex
    }

    [Fact]
    public void GenerateNew_GeneroEsMoF()
    {
        var gen = CreateGen();
        for (int i = 0; i < 20; i++)
        {
            var p = gen.GenerateNew();
            Assert.Contains(p.Gender, new[] { "M", "F" });
        }
    }

    [Fact]
    public void GenerateNew_GrupoEtarioValido()
    {
        var grupos = new[] { "0-14", "15-29", "30-44", "45-64", "65+" };
        var gen    = CreateGen();
        for (int i = 0; i < 30; i++)
        {
            var p = gen.GenerateNew();
            Assert.Contains(p.AgeGroup, grupos);
        }
    }

    [Fact]
    public void GenerateNew_EdadConsistenteConGrupo()
    {
        var gen = CreateGen();
        for (int i = 0; i < 50; i++)
        {
            var p   = gen.GenerateNew();
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            var edad = hoy.Year - p.BirthDate.Year;
            if (p.BirthDate > hoy.AddYears(-edad)) edad--;

            var (min, max) = p.AgeGroup switch
            {
                "0-14"  => (0, 14),
                "15-29" => (15, 29),
                "30-44" => (30, 44),
                "45-64" => (45, 64),
                "65+"   => (65, 90),
                _       => (0, 120)
            };
            Assert.InRange(edad, min, max);
        }
    }

    [Fact]
    public void GenerateNew_NombreYApellidoNoVacios()
    {
        var gen = CreateGen();
        for (int i = 0; i < 10; i++)
        {
            var p = gen.GenerateNew();
            Assert.NotEmpty(p.GivenName);
            Assert.NotEmpty(p.FamilyName);
        }
    }

    [Fact]
    public void GenerateNew_EsNuevoPorDefecto()
    {
        var p = CreateGen().GenerateNew();
        Assert.True(p.EsNuevo);
    }

    [Fact]
    public void GenerateNew_DistribucionGeneroAproximada()
    {
        // GenderRatio default: M=48, F=52 → ~48% masculino
        var gen = CreateGen(seed: 1);
        int masculinos = 0;
        const int n = 500;
        for (int i = 0; i < n; i++)
            if (gen.GenerateNew().Gender == "M") masculinos++;

        Assert.InRange(masculinos, 200, 300); // 40%-60%
    }
}
