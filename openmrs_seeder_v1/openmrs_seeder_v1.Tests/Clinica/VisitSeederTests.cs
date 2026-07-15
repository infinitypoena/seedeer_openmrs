using OpenmrsSeeder.Seeders;

namespace openmrs_seeder_v1.Tests.Clinica;

public class VisitSeederTests
{
    [Theory]
    [InlineData("", "+0000")]           // vacío = UTC (comportamiento histórico)
    [InlineData(null, "+0000")]
    [InlineData("-06:00", "-0600")]     // El Salvador
    [InlineData("-0600", "-0600")]      // ya normalizado
    [InlineData("+05:30", "+0530")]     // India (offset con minutos)
    [InlineData(" -06:00 ", "-0600")]   // con espacios
    public void NormalizarOffset_FormatosValidos(string? entrada, string esperado)
    {
        Assert.Equal(esperado, VisitSeeder.NormalizarOffset(entrada));
    }

    [Theory]
    [InlineData("-6")]
    [InlineData("06:00")]     // sin signo
    [InlineData("-6:00")]     // hora de un dígito
    [InlineData("America/El_Salvador")]
    public void NormalizarOffset_FormatoInvalido_Lanza(string entrada)
    {
        Assert.Throws<FormatException>(() => VisitSeeder.NormalizarOffset(entrada));
    }

    [Fact]
    public void FormatDatetime_UsaElOffsetConfigurado()
    {
        var original = VisitSeeder.UtcOffset;
        try
        {
            var dt = new DateTime(2025, 4, 15, 8, 30, 0);

            VisitSeeder.UtcOffset = "+0000";
            Assert.Equal("2025-04-15T08:30:00.000+0000", VisitSeeder.FormatDatetime(dt));

            VisitSeeder.UtcOffset = "-0600";
            // La hora de pared NO cambia — solo el offset declarado (8:30 locales de El Salvador)
            Assert.Equal("2025-04-15T08:30:00.000-0600", VisitSeeder.FormatDatetime(dt));
        }
        finally
        {
            VisitSeeder.UtcOffset = original;
        }
    }
}
