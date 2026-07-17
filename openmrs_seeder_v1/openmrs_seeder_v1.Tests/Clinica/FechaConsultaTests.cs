using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Seeders;
using Xunit;

namespace openmrs_seeder_v1.Tests.Clinica;

/// <summary>
/// <c>ConsultaSeeder.FechaConsulta</c> es el instante compartido del encuentro de consulta (llegada +
/// 30 min). Todo lo que cuelga de la consulta (obs, órdenes con <c>dateActivated</c>) usa ESTE instante:
/// OpenMRS rechaza una obs anterior a su encuentro y una orden anterior al suyo
/// (<c>Order.error.encounterDatetimeAfterDateActivated</c>).
/// </summary>
public class FechaConsultaTests
{
    [Fact]
    public void EsLaLlegadaMasTreintaMinutos()
    {
        var p = new SimulatedPatient { VisitDatetime = new DateTime(2023, 5, 10, 8, 15, 0) };

        Assert.Equal(new DateTime(2023, 5, 10, 8, 45, 0), ConsultaSeeder.FechaConsulta(p));
    }

    [Fact]
    public void SiempreEsPosteriorALaLlegada()
    {
        // La invariante que protege a las obs: nada fechado en la consulta puede caer antes de la visita.
        var p = new SimulatedPatient { VisitDatetime = new DateTime(2024, 12, 31, 23, 50, 0) };

        Assert.True(ConsultaSeeder.FechaConsulta(p) > p.VisitDatetime);
    }
}
