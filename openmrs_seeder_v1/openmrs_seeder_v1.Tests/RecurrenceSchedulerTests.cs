using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class RecurrenceSchedulerTests
{
    private static readonly RecurrenceSettings Def = new();

    [Fact]
    public void Agudo_CaeEnBanda_7a21()
    {
        var rng = new Random(1);
        var baseDate = new DateOnly(2024, 6, 1);
        for (int i = 0; i < 500; i++)
        {
            var next = RecurrenceScheduler.ProximaFechaElegible(baseDate, esCronico: false, rng, Def);
            var gap = next.DayNumber - baseDate.DayNumber;
            Assert.InRange(gap, Def.MinDiasAgudo, Def.MaxDiasAgudo);
        }
    }

    [Fact]
    public void Cronico_CaeEnBanda_30a120()
    {
        var rng = new Random(2);
        var baseDate = new DateOnly(2024, 6, 1);
        for (int i = 0; i < 500; i++)
        {
            var next = RecurrenceScheduler.ProximaFechaElegible(baseDate, esCronico: true, rng, Def);
            var gap = next.DayNumber - baseDate.DayNumber;
            Assert.InRange(gap, Def.MinDiasCronico, Def.MaxDiasCronico);
        }
    }

    [Fact]
    public void ProximaFecha_SiempreDespuesDeLaUltimaVisita()
    {
        var rng = new Random(3);
        var baseDate = new DateOnly(2024, 1, 15);
        for (int i = 0; i < 200; i++)
        {
            var agudo   = RecurrenceScheduler.ProximaFechaElegible(baseDate, false, rng, Def);
            var cronico = RecurrenceScheduler.ProximaFechaElegible(baseDate, true,  rng, Def);
            Assert.True(agudo   > baseDate);
            Assert.True(cronico > baseDate);
        }
    }

    [Fact]
    public void CronicoEspaciaMasQueAgudo_EnPromedio()
    {
        var rng = new Random(4);
        var baseDate = new DateOnly(2024, 6, 1);
        double sumaAgudo = 0, sumaCronico = 0;
        const int n = 2000;
        for (int i = 0; i < n; i++)
        {
            sumaAgudo   += RecurrenceScheduler.ProximaFechaElegible(baseDate, false, rng, Def).DayNumber - baseDate.DayNumber;
            sumaCronico += RecurrenceScheduler.ProximaFechaElegible(baseDate, true,  rng, Def).DayNumber - baseDate.DayNumber;
        }
        Assert.True(sumaCronico / n > sumaAgudo / n);
    }

    [Fact]
    public void BandaInvertida_NoLanza_UsaMinimo()
    {
        // Config defensiva: si max < min, el intervalo es exactamente min (no excepción de Random.Next).
        var s = new RecurrenceSettings { MinDiasAgudo = 10, MaxDiasAgudo = 3 };
        var next = RecurrenceScheduler.ProximaFechaElegible(new DateOnly(2024, 6, 1), false, new Random(5), s);
        Assert.Equal(new DateOnly(2024, 6, 11), next);
    }
}
