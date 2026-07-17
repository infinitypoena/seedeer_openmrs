using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Modelo;

/// <summary>
/// <c>PesoMedioDeApertura</c> es el denominador con el que <c>BassGrowthModel.CapacidadDelDia</c> escala
/// el aforo: sin él, "25/día" acabaría siendo 24,2 de media y el objetivo no significaría lo que dice.
/// Solo promedia los días que ABREN (el domingo a 0 no diluye la media).
/// </summary>
public class PesoMedioDeAperturaTests
{
    [Fact]
    public void PromediaSoloLosDiasQueAbren()
    {
        // Pesos por defecto: 1,2 + 1,2 + 1,0 + 1,0 + 0,9 + 0,5 = 5,8 entre SEIS días (el domingo no cuenta).
        var w = new WeekdayWeightsSettings();

        Assert.Equal(5.8 / 6, DailyScheduleGenerator.PesoMedioDeApertura(w), precision: 10);
    }

    [Fact]
    public void ConLaSemanaUniforme_LaMediaEsElPropioPeso()
    {
        var w = new WeekdayWeightsSettings
        {
            Monday = 0.7, Tuesday = 0.7, Wednesday = 0.7, Thursday = 0.7,
            Friday = 0.7, Saturday = 0.7, Sunday = 0.7,
        };

        Assert.Equal(0.7, DailyScheduleGenerator.PesoMedioDeApertura(w), precision: 10);
    }

    [Fact]
    public void ConTodoCerrado_DevuelveUno_NoDividePorCero()
    {
        var w = new WeekdayWeightsSettings
        {
            Monday = 0, Tuesday = 0, Wednesday = 0, Thursday = 0,
            Friday = 0, Saturday = 0, Sunday = 0,
        };

        Assert.Equal(1.0, DailyScheduleGenerator.PesoMedioDeApertura(w));
    }

    [Fact]
    public void ElAforoEscaladoPorElPesoMedio_PromediaExactamenteElObjetivo()
    {
        // La propiedad que el denominador garantiza: sumando el aforo de una semana entera, la media de
        // los días abiertos cae en el objetivo (con la tolerancia del redondeo a pacientes enteros).
        var sim = TestSupport.Escenarios.SimObjetivo(objetivo: 25);
        var w   = sim.WeekdayWeights;

        var abiertos = Enum.GetValues<DayOfWeek>()
            .Select(d => DailyScheduleGenerator.PesoDelDia(d, w))
            .Where(p => p > 0)
            .Select(p => BassGrowthModel.CapacidadDelDia(sim, p))
            .ToList();

        Assert.InRange(abiertos.Average(), 24.0, 26.0);
    }
}
