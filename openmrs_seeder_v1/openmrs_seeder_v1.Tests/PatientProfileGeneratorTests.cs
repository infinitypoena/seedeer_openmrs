using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class PatientProfileGeneratorTests
{
    private static PatientProfileGenerator CreateGen(int seed = 42) =>
        new(new SimulationSettings { RandomSeed = seed });

    private static int EdadEnMeses(DateOnly birth, DateOnly reference)
    {
        var meses = (reference.Year - birth.Year) * 12 + (reference.Month - birth.Month);
        if (reference.Day < birth.Day) meses--;
        return meses;
    }

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
    public void GenerateNew_ConFechaPasada_NacimientoNuncaDespuesDeLaVisita()
    {
        var gen = CreateGen();
        var refDate = new DateOnly(2023, 6, 1);
        for (int i = 0; i < 200; i++)
        {
            var p = gen.GenerateNew(refDate);
            Assert.True(p.BirthDate <= refDate,
                $"Nacimiento {p.BirthDate} no debe ser posterior a la visita {refDate}");
        }
    }

    [Fact]
    public void GenerateNew_RespetaEdadMinima_6Meses()
    {
        var gen = CreateGen();
        var refDate = new DateOnly(2023, 6, 1);
        for (int i = 0; i < 300; i++)
        {
            var p = gen.GenerateNew(refDate);
            Assert.True(EdadEnMeses(p.BirthDate, refDate) >= 6,
                $"Edad < 6 meses: nacimiento {p.BirthDate}, grupo {p.AgeGroup}");
        }
    }

    [Fact]
    public void GenerateNew_ModoPediatrico_PermiteDesde1Mes()
    {
        var settings = new SimulationSettings { RandomSeed = 7 };
        settings.DemographicProfile.PediatricClinic = true;
        // Forzar solo el grupo 0-14 para ejercitar el mínimo pediátrico
        settings.DemographicProfile.AgeGroups = [new() { Label = "0-14", Weight = 100 }];
        var gen = new PatientProfileGenerator(settings);
        var refDate = new DateOnly(2023, 6, 1);

        bool huboLactanteMenor6 = false;
        for (int i = 0; i < 400; i++)
        {
            var p = gen.GenerateNew(refDate);
            var meses = EdadEnMeses(p.BirthDate, refDate);
            Assert.True(meses >= 1, $"En pediatría el mínimo es 1 mes; obtuve {meses}");
            if (meses < 6) huboLactanteMenor6 = true;
        }
        Assert.True(huboLactanteMenor6, "En modo pediátrico deberían aparecer lactantes < 6 meses");
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
