using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Seeders;

namespace openmrs_seeder_v1.Tests.Clinica;

public class ConsultaSeederTests
{
    [Fact]
    public void ValorExamenNumerico_BandaEntera_DevuelveEnteroDentroDeLaBanda()
    {
        // Glasgow 12-15: límites enteros → valor entero (los conceptos de puntaje no admiten decimales)
        var examen = new ExamenClinicoEntry { ResMin = 12, ResMax = 15 };
        var rng = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            var v = ConsultaSeeder.ValorExamenNumerico(examen, rng);
            Assert.InRange(v, 12, 15);
            Assert.Equal(v, Math.Round(v)); // sin decimales
        }
    }

    [Fact]
    public void ValorExamenNumerico_BandaDecimal_ConservaUnDecimal()
    {
        var examen = new ExamenClinicoEntry { ResMin = 0.5, ResMax = 1.3 };
        var rng = new Random(42);

        for (int i = 0; i < 50; i++)
        {
            var v = ConsultaSeeder.ValorExamenNumerico(examen, rng);
            Assert.InRange(v, 0.5, 1.3);
        }
    }
}
