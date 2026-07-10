using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests;

public class ErrorTallyTests
{
    [Fact]
    public void Registrar_AcumulaPorFuenteYTotal()
    {
        var tally = new ErrorTally();

        tally.Registrar("LabOrderSeeder", "obs rechazada");
        tally.Registrar("LabOrderSeeder", "otra obs rechazada");
        tally.Registrar("VitalsSeeder", "encounter falló");

        Assert.Equal(3, tally.Total);
        Assert.Equal(2, tally.PorFuente["LabOrderSeeder"]);
        Assert.Equal(1, tally.PorFuente["VitalsSeeder"]);
    }

    [Fact]
    public void Reset_LimpiaContadoresYMensajes()
    {
        var tally = new ErrorTally();
        tally.Registrar("X", "error");

        tally.Reset();

        Assert.Equal(0, tally.Total);
        Assert.Empty(tally.Mensajes);
    }

    [Fact]
    public void Registrar_RecortaMensajesLargos()
    {
        // Los errores REST de OpenMRS traen el stacktrace Java completo (miles de caracteres)
        var tally = new ErrorTally();
        tally.Registrar("LabOrderSeeder", new string('x', 5000));

        var mensaje = Assert.Single(tally.Mensajes);
        Assert.True(mensaje.Length < 300, $"Mensaje no recortado: {mensaje.Length} chars");
        Assert.EndsWith("…", mensaje);
    }

    [Fact]
    public void Desglose_OrdenaPorConteoDescendente()
    {
        var tally = new ErrorTally();
        tally.Registrar("B", "e");
        tally.Registrar("A", "e");
        tally.Registrar("A", "e");

        Assert.Equal("A: 2, B: 1", tally.Desglose());
    }

    [Fact]
    public void Provider_CuentaSoloNivelErrorOSuperior()
    {
        var tally = new ErrorTally();
        using var provider = new ErrorTallyLoggerProvider(tally);
        var logger = provider.CreateLogger("OpenmrsSeeder.Seeders.LabOrderSeeder");

        logger.LogInformation("info — no cuenta");
        logger.LogWarning("warning — no cuenta");
        logger.LogError("error — cuenta");
        logger.LogCritical("critical — cuenta");

        Assert.Equal(2, tally.Total);
    }

    [Fact]
    public void Provider_UsaUltimoSegmentoDeLaCategoria()
    {
        var tally = new ErrorTally();
        using var provider = new ErrorTallyLoggerProvider(tally);

        provider.CreateLogger("OpenmrsSeeder.Seeders.LabOrderSeeder").LogError("falló");
        provider.CreateLogger("Seeder").LogError("falló");

        Assert.Equal(1, tally.PorFuente["LabOrderSeeder"]);
        Assert.Equal(1, tally.PorFuente["Seeder"]);
    }
}
