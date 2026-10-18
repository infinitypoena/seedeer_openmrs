using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Clinica;

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

    // ── Talla teórica por edad (WHO) ──────────────────────────────────────────

    [Fact]
    public void TallaTeorica_MedianaWhoPorSexoEsPlausible()
    {
        var nino0    = VitalsSeeder.TallaTeoricaMedianaCm("M", 0);
        var nino168  = VitalsSeeder.TallaTeoricaMedianaCm("M", 14 * 12);
        var nina168  = VitalsSeeder.TallaTeoricaMedianaCm("F", 14 * 12);

        Assert.InRange(nino0, 48, 52);        // recién nacido ~50 cm
        Assert.InRange(nino168, 150, 175);    // adolescente, no lactante
        Assert.True(nina168 < nino168);       // en la pubertad los niños superan a las niñas
    }

    [Fact]
    public void TallaTeorica_EsMonotonaCrecientePorSexo()
    {
        double prevM = -1, prevF = -1;
        foreach (var m in new[] { 0, 6, 12, 24, 36, 60, 120, 168 })
        {
            var hM = VitalsSeeder.TallaTeoricaMedianaCm("M", m);
            var hF = VitalsSeeder.TallaTeoricaMedianaCm("F", m);
            Assert.True(hM > prevM, $"niño en {m} meses no crece");
            Assert.True(hF > prevF, $"niña en {m} meses no crece");
            prevM = hM; prevF = hF;
        }
    }

    [Fact]
    public void SigmaTalla_CreceConLaEdad()
    {
        Assert.True(VitalsSeeder.SigmaTallaCm("M", 0) < VitalsSeeder.SigmaTallaCm("M", 14 * 12));
    }

    [Fact]
    public void DecrementoSenil_PierdeTallaConLaEdad()
    {
        Assert.Equal(0.0, VitalsSeeder.DecrementoTallaSenilCm(50 * 12));      // a los 50 aún no
        Assert.Equal(0.0, VitalsSeeder.DecrementoTallaSenilCm(40 * 12));
        Assert.True(VitalsSeeder.DecrementoTallaSenilCm(85 * 12) > 1.0);      // anciano pierde >1 cm
        Assert.InRange(VitalsSeeder.DecrementoTallaSenilCm(120 * 12), 0.0, 3.0); // tope
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

    [Fact]
    public void Lactante_TallaCercanaAlaMedianaWho()
    {
        // Un bebé de 6 meses ronda la mediana WHO (~67 cm), no sale con talla de niño grande ni de recién nacido.
        var rng = new Random(7);
        for (int i = 0; i < 300; i++)
        {
            var v = VitalsSeeder.ComputeVitals(["respiratorio"], 1, "M", "0-14", false, null, null, NoClima, rng,
                null, null, null, tallaFijaCm: null, imcBasal: null, edadMeses: 6);
            Assert.InRange(v.HeightCm, 55, 80);
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
