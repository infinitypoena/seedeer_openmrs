using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class AntropometriaEdadTests
{
    private static readonly ClimateSettings NoClima = new();

    // ── GrupoEdad: recalculado desde la fecha de nacimiento ───────────────────

    [Theory]
    [InlineData(10, "0-14")]
    [InlineData(14, "0-14")]
    [InlineData(15, "15-29")]
    [InlineData(29, "15-29")]
    [InlineData(30, "30-44")]
    [InlineData(44, "30-44")]
    [InlineData(45, "45-64")]
    [InlineData(64, "45-64")]
    [InlineData(65, "65+")]
    [InlineData(90, "65+")]
    public void GrupoEdad_MapeaEdadAFranja(int edad, string esperado)
    {
        var fecha = new DateOnly(2024, 6, 15);
        var nacimiento = fecha.AddYears(-edad).AddDays(-10); // cumpleaños ya pasado
        Assert.Equal(esperado, PatientProfileGenerator.GrupoEdad(nacimiento, fecha));
    }

    [Fact]
    public void GrupoEdad_AvanzaConElTiempo_CruceDeFranja()
    {
        var nacimiento = new DateOnly(1994, 6, 20); // cumple 30 el 2024-06-20
        Assert.Equal("15-29", PatientProfileGenerator.GrupoEdad(nacimiento, new DateOnly(2024, 6, 19)));
        Assert.Equal("30-44", PatientProfileGenerator.GrupoEdad(nacimiento, new DateOnly(2024, 6, 20)));
    }

    [Fact]
    public void EdadEnMeses_CuentaCumplidos()
    {
        var nac = new DateOnly(2024, 1, 15);
        Assert.Equal(6, PatientProfileGenerator.EdadEnMeses(nac, new DateOnly(2024, 7, 15)));
        Assert.Equal(5, PatientProfileGenerator.EdadEnMeses(nac, new DateOnly(2024, 7, 14))); // aún no cumple el mes
    }

    // ── Talla pediátrica por edad ─────────────────────────────────────────────

    [Fact]
    public void TallaPediatrica_EsMonotonaYPlausible()
    {
        var t0  = VitalsSeeder.TallaPediatricaCm(0);
        var t6  = VitalsSeeder.TallaPediatricaCm(6);
        var t12 = VitalsSeeder.TallaPediatricaCm(12);
        var t168 = VitalsSeeder.TallaPediatricaCm(14 * 12);

        Assert.InRange(t0, 48, 52);
        Assert.True(t6 < 90);            // ningún lactante con talla de adolescente
        Assert.True(t0 < t6 && t6 < t12 && t12 < t168); // monótona creciente
        Assert.InRange(t168, 150, 175);
    }

    [Fact]
    public void Lactante_NoSuperaTallaDeAdolescente()
    {
        // Un bebé de 6 meses (con edadMeses) nunca sale con 155 cm como en la banda plana 90-160.
        var rng = new Random(9);
        for (int i = 0; i < 300; i++)
        {
            var v = VitalsSeeder.ComputeVitals(["respiratorio"], 1, "F", "0-14", false, null, null, NoClima, rng,
                null, null, null, tallaFijaCm: null, imcBasal: null, edadMeses: 6);
            Assert.True(v.HeightCm < 90);
        }
    }

    // ── Persistencia: talla constante e IMC basal estable ─────────────────────

    [Fact]
    public void TallaFija_SeRespetaEntreVisitas()
    {
        var rng = new Random(11);
        // Con talla fijada, ComputeVitals la devuelve tal cual (no la re-sortea).
        for (int i = 0; i < 200; i++)
        {
            var v = VitalsSeeder.ComputeVitals(["diabetes"], 1, "M", "45-64", false, null, null, NoClima, rng,
                null, null, null, tallaFijaCm: 170.0, imcBasal: 30.0, edadMeses: null);
            Assert.Equal(170.0, v.HeightCm);
        }
    }

    [Fact]
    public void PesoDerivaPoco_AlrededorDelImcBasal()
    {
        var rng = new Random(12);
        const double talla = 170.0, imcBasal = 30.0;
        for (int i = 0; i < 500; i++)
        {
            var v = VitalsSeeder.ComputeVitals(["diabetes"], 1, "M", "45-64", false, null, null, NoClima, rng,
                null, null, null, tallaFijaCm: talla, imcBasal: imcBasal, edadMeses: null);
            var imc = v.WeightKg / Math.Pow(v.HeightCm / 100.0, 2);
            Assert.InRange(imc, imcBasal - 1.6, imcBasal + 1.6); // deriva ±1.5
        }
    }
}
