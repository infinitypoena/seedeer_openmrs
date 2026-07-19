using OpenmrsSeeder.Seeders;

namespace openmrs_seeder_v1.Tests.Clinica;

public class PatientSeederTests
{
    [Fact]
    public void FormatearBirthdate_VaAMediodia_NuncaAMedianoche()
    {
        // "1988-05-01" a las 00:00 no existe en America/El_Salvador (transición DST de 1988): mandado a
        // medianoche, OpenMRS respondía 400 y el paciente se perdía (corrida del 17-jul-2026, único error
        // en 23.263 visitas). A mediodía el instante siempre existe y la fecha almacenada es la misma.
        var s = PatientSeeder.FormatearBirthdate(new DateOnly(1988, 5, 1));

        Assert.StartsWith("1988-05-01T12:00:00.000", s);
    }

    [Fact]
    public void FormatearBirthdate_ConservaLaFecha()
    {
        var s = PatientSeeder.FormatearBirthdate(new DateOnly(2001, 12, 31));

        Assert.StartsWith("2001-12-31T12:00:00.000", s);
    }
}
