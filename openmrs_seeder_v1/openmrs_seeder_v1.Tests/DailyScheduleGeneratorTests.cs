using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class DailyScheduleGeneratorTests
{
    private static SimulationSettings CreateSettings(
        string start = "2023-01-01",
        string end   = "2023-01-31",
        int medio    = 20,
        int seed     = 42) => new()
    {
        StartDate            = DateTime.Parse(start),
        EndDate              = DateTime.Parse(end),
        PacientesPorDiaMedio = medio,
        PorcentajeRecurrentes = 30,
        RandomSeed           = seed
    };

    [Fact]
    public void Generate_DomingoSiempreCeroPacientes()
    {
        var gen  = new DailyScheduleGenerator(CreateSettings());
        var days = gen.Generate();

        var domingos = days.Where(d => d.Date.DayOfWeek == DayOfWeek.Sunday);
        Assert.All(domingos, d => Assert.Equal(0, d.TotalPatients));
    }

    [Fact]
    public void Generate_RangoDebieneConocidos()
    {
        var gen  = new DailyScheduleGenerator(CreateSettings(start: "2023-01-01", end: "2023-01-07"));
        var days = gen.Generate();

        Assert.Equal(7, days.Count);
        Assert.Equal(new DateOnly(2023, 1, 1), days.First().Date);
        Assert.Equal(new DateOnly(2023, 1, 7), days.Last().Date);
    }

    [Fact]
    public void Generate_SplitNuevosRecurrentesSumaTotal()
    {
        var gen  = new DailyScheduleGenerator(CreateSettings());
        var days = gen.Generate();

        foreach (var d in days)
            Assert.Equal(d.TotalPatients, d.NuevosPacientes + d.PacientesRecurrentes);
    }

    [Fact]
    public void Generate_VolumenPromedioCercaDelParametro()
    {
        // Con seed fija el promedio debe estar dentro del ±40% del parámetro
        var gen   = new DailyScheduleGenerator(CreateSettings(medio: 40, seed: 42));
        var days  = gen.Generate();
        var diasHabiles = days.Where(d => d.TotalPatients > 0).ToList();

        var promedio = diasHabiles.Average(d => (double)d.TotalPatients);
        Assert.InRange(promedio, 24, 56);  // ±40% de 40
    }

    [Fact]
    public void Generate_MismaSeedProduceResultadoIdentico()
    {
        var gen1 = new DailyScheduleGenerator(CreateSettings(seed: 99));
        var gen2 = new DailyScheduleGenerator(CreateSettings(seed: 99));

        var days1 = gen1.Generate().Select(d => d.TotalPatients).ToList();
        var days2 = gen2.Generate().Select(d => d.TotalPatients).ToList();

        Assert.Equal(days1, days2);
    }

    [Fact]
    public void Generate_SeedDistintaProduceResultadoDiferente()
    {
        var gen1 = new DailyScheduleGenerator(CreateSettings(seed: 10));
        var gen2 = new DailyScheduleGenerator(CreateSettings(seed: 99));

        var days1 = gen1.Generate().Select(d => d.TotalPatients).ToList();
        var days2 = gen2.Generate().Select(d => d.TotalPatients).ToList();

        Assert.False(days1.SequenceEqual(days2));
    }

    [Fact]
    public void GenerateVisitTime_HoraEnRangoDiurno()
    {
        var gen = new DailyScheduleGenerator(CreateSettings());
        var fecha = new DateOnly(2023, 3, 15);

        for (int i = 0; i < 50; i++)
        {
            var dt = gen.GenerateVisitTime(fecha);
            Assert.Equal(fecha, DateOnly.FromDateTime(dt));
            Assert.InRange(dt.Hour, 7, 18);
        }
    }

    [Fact]
    public void GenerateVisitTime_DistribucionConcentradaEnPicos()
    {
        var gen = new DailyScheduleGenerator(CreateSettings(seed: 0));
        var fecha = new DateOnly(2023, 3, 15);

        int enPicoAM = 0, enPicoPM = 0, enResto = 0;
        for (int i = 0; i < 1000; i++)
        {
            var dt = gen.GenerateVisitTime(fecha);
            if (dt.Hour >= 8 && dt.Hour < 10) enPicoAM++;
            else if (dt.Hour >= 14 && dt.Hour < 16) enPicoPM++;
            else enResto++;
        }

        // ~40% pico AM, ~30% pico PM, ~30% resto — tolerancia de ±10%
        Assert.InRange(enPicoAM, 280, 520);
        Assert.InRange(enPicoPM, 180, 420);
    }
}
