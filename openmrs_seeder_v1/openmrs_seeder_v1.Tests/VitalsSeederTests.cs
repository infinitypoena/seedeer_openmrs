using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Seeders;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class VitalsSeederTests
{
    private static readonly ClimateSettings NoClima = new();

    /// <summary>Ejecuta ComputeVitals N veces (semilla fija) y devuelve los resultados.</summary>
    private static IEnumerable<VitalsSeeder.VitalSigns> Run(
        string[] categorias, int sev = 1, string gender = "M", string ageGroup = "30-44",
        bool fiebre = false, string? imc = null, int n = 500)
    {
        var rng = new Random(123);
        for (var i = 0; i < n; i++)
            yield return VitalsSeeder.ComputeVitals(categorias, sev, gender, ageGroup, fiebre, imc, null, NoClima, rng);
    }

    private static double Imc(VitalsSeeder.VitalSigns v) => v.WeightKg / Math.Pow(v.HeightCm / 100.0, 2);

    [Fact]
    public void SeverityRank_MapeaTexto()
    {
        Assert.Equal(3, VitalsSeeder.SeverityRank("grave"));
        Assert.Equal(2, VitalsSeeder.SeverityRank("moderado"));
        Assert.Equal(1, VitalsSeeder.SeverityRank("leve"));
        Assert.Equal(1, VitalsSeeder.SeverityRank(null));
    }

    [Fact]
    public void Diabetes_DaSobrepesoUObesidad()
    {
        // IMC objetivo 27–38 (margen por redondeo del peso a 1 decimal)
        Assert.All(Run(["diabetes"]), v => Assert.InRange(Imc(v), 26.5, 38.5));
    }

    [Fact]
    public void SinEnfermedadRelevante_ImcNormal()
    {
        Assert.All(Run(["osteomuscular"]), v => Assert.InRange(Imc(v), 18.0, 27.6));
    }

    [Fact]
    public void OverrideImcBajo_DaBajoPeso()
    {
        // p.ej. hipertiroidismo: categoría endocrino daría alto, pero el override manda
        Assert.All(Run(["endocrino"], imc: "bajo"), v => Assert.InRange(Imc(v), 15.5, 19.5));
    }

    [Fact]
    public void Infeccioso_DaFiebre()
    {
        Assert.All(Run(["infeccioso"]), v => Assert.True(v.TempC >= 37.5));
    }

    [Fact]
    public void RespiratorioGrave_DaFiebreYBajaSpO2()
    {
        Assert.All(Run(["respiratorio"], sev: 3), v =>
        {
            Assert.True(v.TempC >= 37.5);          // respiratorio + sev≥2 → febril
            Assert.InRange(v.SpO2, 88, 93);
        });
    }

    [Fact]
    public void OverrideFiebre_FuerzaTemperaturaAunqueCategoriaNoFebril()
    {
        // digestivo no es febril por categoría; el override sí
        Assert.All(Run(["digestivo"], fiebre: true), v => Assert.True(v.TempC >= 37.5));
    }

    [Fact]
    public void Cardiovascular_DaHipertension()
    {
        Assert.All(Run(["cardiovascular"]), v =>
        {
            Assert.True(v.Systolic >= 140);
            Assert.True(v.Diastolic >= 90);
        });
    }

    [Fact]
    public void Comorbilidad_UsaUnionDeCategorias()
    {
        // primario respiratorio + comorbilidad diabetes → fiebre (sev grave) Y sobrepeso
        Assert.All(Run(["respiratorio", "diabetes"], sev: 3), v =>
        {
            Assert.True(v.TempC >= 37.5);
            Assert.True(Imc(v) >= 26.5);
        });
    }

    [Fact]
    public void SinEnfermedad_VitalesNormales()
    {
        Assert.All(Run(["osteomuscular"]), v =>
        {
            Assert.InRange(v.TempC, 36.0, 37.4);
            Assert.InRange(v.SpO2, 95, 99);
            Assert.InRange(v.RespRate, 12, 20);
            Assert.InRange(v.Pulse, 60, 100);
        });
    }

    [Fact]
    public void Pediatrico_TallaYPesoPlausibles()
    {
        Assert.All(Run(["respiratorio"], ageGroup: "0-14", sev: 1), v =>
        {
            Assert.InRange(v.HeightCm, 90, 160);
            Assert.InRange(Imc(v), 13.5, 20.5);
        });
    }
}
