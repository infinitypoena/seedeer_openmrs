using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Seeders;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class AppointmentSeederTests
{
    private static readonly DateTime Visita = new(2025, 2, 15, 9, 0, 0);
    private const int Tolerancia = 3;

    private static CitaPendiente Cita(string uuid, DateTime fecha) => new(uuid, fecha);

    [Fact]
    public void CitaDentroDeTolerancia_SeCompleta()
    {
        var citas = new[] { Cita("a", Visita.AddDays(2)), Cita("b", Visita.AddDays(-3)) };
        var (completar, perder, conservar) = AppointmentSeeder.ClasificarCitas(citas, Visita, Tolerancia);
        Assert.Equal(2, completar.Count);
        Assert.Empty(perder);
        Assert.Empty(conservar);
    }

    [Fact]
    public void CitaVencida_SePierde()
    {
        var citas = new[] { Cita("a", Visita.AddDays(-10)) };
        var (completar, perder, conservar) = AppointmentSeeder.ClasificarCitas(citas, Visita, Tolerancia);
        Assert.Empty(completar);
        Assert.Single(perder);
        Assert.Equal("a", perder[0].Uuid);
        Assert.Empty(conservar);
    }

    [Fact]
    public void CitaFutura_SeConserva()
    {
        var citas = new[] { Cita("a", Visita.AddDays(20)) };
        var (completar, perder, conservar) = AppointmentSeeder.ClasificarCitas(citas, Visita, Tolerancia);
        Assert.Empty(completar);
        Assert.Empty(perder);
        Assert.Single(conservar);
    }

    [Fact]
    public void MezclaDeCitas_SeClasificaCorrectamente()
    {
        var citas = new[]
        {
            Cita("hoy",     Visita),                 // exacta → completar
            Cita("cercana", Visita.AddDays(3)),      // borde tolerancia → completar
            Cita("vencida", Visita.AddDays(-4)),     // justo fuera → perder
            Cita("futura",  Visita.AddDays(4))       // justo fuera, futura → conservar
        };
        var (completar, perder, conservar) = AppointmentSeeder.ClasificarCitas(citas, Visita, Tolerancia);
        Assert.Equal(["hoy", "cercana"], completar.Select(c => c.Uuid));
        Assert.Equal(["vencida"], perder.Select(c => c.Uuid));
        Assert.Equal(["futura"], conservar.Select(c => c.Uuid));
    }

    [Fact]
    public void SinCitas_TodoVacio()
    {
        var (completar, perder, conservar) = AppointmentSeeder.ClasificarCitas([], Visita, Tolerancia);
        Assert.Empty(completar);
        Assert.Empty(perder);
        Assert.Empty(conservar);
    }

    [Fact]
    public void ComparaPorFecha_IgnorandoHora()
    {
        // La cita se agenda a una hora del día; la clasificación compara solo fechas.
        var citas = new[] { Cita("a", Visita.Date.AddDays(-Tolerancia).AddHours(14)) };
        var (completar, _, _) = AppointmentSeeder.ClasificarCitas(citas, Visita, Tolerancia);
        Assert.Single(completar);
    }
}
