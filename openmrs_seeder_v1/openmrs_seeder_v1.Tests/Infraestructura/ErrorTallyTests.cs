using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests.Infraestructura;

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
    public void TrasMarcarCancelacion_NoContabiliza()
    {
        // Ctrl+C: las POST en vuelo que se cancelan lanzan OperationCanceledException que los seeders
        // registran como LogError; tras la cancelación no deben inflar el resumen.
        var tally = new ErrorTally();
        tally.Registrar("LabOrderSeeder", "error real antes de cancelar");

        tally.MarcarCancelacion();
        tally.Registrar("LabOrderSeeder", "TaskCanceledException");
        tally.Registrar("VitalsSeeder", "TaskCanceledException");

        Assert.Equal(1, tally.Total);
    }

    [Fact]
    public void Reset_ReactivaElConteo_TrasCancelacion()
    {
        var tally = new ErrorTally();
        tally.MarcarCancelacion();
        tally.Reset();

        tally.Registrar("X", "error de una corrida nueva");
        Assert.Equal(1, tally.Total);
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

    [Fact]
    public void Provider_ClasificaPorLaExcepcionDelLogError()
    {
        var tally = new ErrorTally();
        using var provider = new ErrorTallyLoggerProvider(tally);
        var logger = provider.CreateLogger("OpenmrsSeeder.Seeders.LabOrderSeeder");

        logger.LogError(new HttpRequestException("400", null, System.Net.HttpStatusCode.BadRequest), "rechazada");
        logger.LogError(new HttpRequestException("400", null, System.Net.HttpStatusCode.BadRequest), "rechazada");
        logger.LogError(new HttpRequestException("connection refused"), "sin red");
        logger.LogError("sin excepción — clasifica Otro");

        var desglose = tally.PorFuenteYTipo();
        Assert.Equal(("LabOrderSeeder", TipoError.DatoRechazado, 2), desglose[0]);   // ordenado por conteo
        Assert.Contains(("LabOrderSeeder", TipoError.Red, 1), desglose);
        Assert.Contains(("LabOrderSeeder", TipoError.Otro, 1), desglose);
    }

    [Fact]
    public void Provider_WarningConItemPerdido_CuentaComoPerdida()
    {
        var tally = new ErrorTally();
        using var provider = new ErrorTallyLoggerProvider(tally);
        var logger = provider.CreateLogger("OpenmrsSeeder.Seeders.VitalsSeeder");

        logger.LogWarning(Eventos.ItemPerdido, "Skip obs: encounter no creado");

        Assert.Equal(0, tally.Total);           // no es un error: es un dato que no quedó
        Assert.Equal(1, tally.TotalPerdidas);
        Assert.Equal(1, tally.PerdidasPorFuente["VitalsSeeder"]);
        Assert.Contains(tally.Mensajes, m => m.StartsWith("[VitalsSeeder·perdido]"));
    }

    [Fact]
    public void Provider_WarningSinEventId_NoCuenta()
    {
        // "Visita activa reutilizada", catálogo opcional vacío…: informativos, no pérdidas
        var tally = new ErrorTally();
        using var provider = new ErrorTallyLoggerProvider(tally);

        provider.CreateLogger("OpenmrsSeeder.Seeders.VisitSeeder").LogWarning("Visita activa reutilizada");

        Assert.Equal(0, tally.Total);
        Assert.Equal(0, tally.TotalPerdidas);
    }

    [Fact]
    public void Sink_RecibeElMensajeCompletoSinRecorte()
    {
        // El recorte de 220 es para los mensajes en memoria; el CSV necesita el mensaje entero
        var tally = new ErrorTally(() => new DateTime(2026, 7, 17, 9, 0, 0));
        var recibidos = new List<EventoDeError>();
        tally.Sink = recibidos.Add;

        var largo = new string('x', 5000);
        tally.Registrar("LabOrderSeeder", largo, new HttpRequestException("e", null, System.Net.HttpStatusCode.BadRequest));
        tally.RegistrarPerdida("VitalsSeeder", "skip");

        Assert.Equal(2, recibidos.Count);
        Assert.Equal(largo, recibidos[0].Mensaje);
        Assert.Equal(TipoError.DatoRechazado, recibidos[0].Tipo);
        Assert.False(recibidos[0].EsPerdida);
        Assert.Equal(new DateTime(2026, 7, 17, 9, 0, 0), recibidos[0].Momento);
        Assert.True(recibidos[1].EsPerdida);
    }

    [Fact]
    public void SinkQueLanza_NoTumbaElRegistro()
    {
        // El CSV es evidencia, no la simulación: un disco lleno no puede parar la corrida
        var tally = new ErrorTally();
        tally.Sink = _ => throw new IOException("disco lleno");

        tally.Registrar("X", "error");
        tally.RegistrarPerdida("X", "skip");

        Assert.Equal(1, tally.Total);
        Assert.Equal(1, tally.TotalPerdidas);
    }

    [Fact]
    public void TrasMarcarCancelacion_NiPerdidasNiSink()
    {
        // La cascada del Ctrl+C no debe inflar el resumen NI el CSV
        var tally = new ErrorTally();
        var recibidos = new List<EventoDeError>();
        tally.Sink = recibidos.Add;

        tally.MarcarCancelacion();
        tally.Registrar("LabOrderSeeder", "TaskCanceledException");
        tally.RegistrarPerdida("VitalsSeeder", "skip por cancelación");

        Assert.Equal(0, tally.Total);
        Assert.Equal(0, tally.TotalPerdidas);
        Assert.Empty(recibidos);
    }

    [Fact]
    public void Reset_LimpiaPerdidasYDesgloses()
    {
        var tally = new ErrorTally();
        tally.Registrar("X", "error", new HttpRequestException("e"));
        tally.RegistrarPerdida("X", "skip");

        tally.Reset();

        Assert.Equal(0, tally.TotalPerdidas);
        Assert.Equal(0, tally.EventosTotales);
        Assert.Empty(tally.PorFuenteYTipo());
        Assert.Empty(tally.PerdidasPorFuente);
    }

    [Fact]
    public void EventosTotales_DelataElCapDeMensajes()
    {
        // Con más de 100 eventos, la lista de mensajes se corta: el resumen avisa "mostrando 100 de N"
        var tally = new ErrorTally();
        for (var i = 0; i < 120; i++) tally.Registrar("X", $"error {i}");

        Assert.Equal(100, tally.Mensajes.Count);
        Assert.Equal(120, tally.EventosTotales);
    }
}
