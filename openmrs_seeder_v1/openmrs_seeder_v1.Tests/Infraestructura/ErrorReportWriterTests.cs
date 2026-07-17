using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;

namespace openmrs_seeder_v1.Tests.Infraestructura;

/// <summary>
/// errores.csv es la evidencia navegable de la corrida: una fila por error/ítem perdido con el mensaje
/// completo. Mismo credo que los demás escritores de output/: feature off con carpeta vacía, legible
/// en caliente (FileShare) y nunca tumba la corrida.
/// </summary>
public class ErrorReportWriterTests
{
    private static EventoDeError Evento(string mensaje = "falló", bool perdida = false) =>
        new(new DateTime(2026, 7, 17, 10, 30, 5), "LabOrderSeeder",
            perdida ? TipoError.Otro : TipoError.DatoRechazado, perdida, mensaje);

    [Fact]
    public void CarpetaVacia_FeatureApagada_EscribirNoLanza()
    {
        using var writer = new ErrorReportWriter(null);
        Assert.Null(writer.Ruta);
        writer.Escribir(Evento());   // no lanza

        using var writer2 = new ErrorReportWriter("   ");
        Assert.Null(writer2.Ruta);
    }

    [Fact]
    public void AlConstruir_CreaElFicheroConSoloLaCabecera()
    {
        using var dir = new TempDir();
        using var writer = new ErrorReportWriter(dir.Ruta);

        Assert.Equal(Path.Combine(dir.Ruta, "errores.csv"), writer.Ruta);
        // Un fichero con solo la cabecera es evidencia positiva de "0 errores"
        var lineas = LeerLineas(writer.Ruta!);
        Assert.Equal(["timestamp,componente,evento,tipo,mensaje"], lineas);
    }

    [Fact]
    public void Escribir_EsLegibleMientrasSigueAbierto()
    {
        // La corrida dura horas: hay que poder mirar el CSV en caliente (FileShare.ReadWrite + flush)
        using var dir = new TempDir();
        using var writer = new ErrorReportWriter(dir.Ruta);

        writer.Escribir(Evento("obs rechazada"));
        writer.Escribir(Evento("skip sin encounter", perdida: true));

        var lineas = LeerLineas(writer.Ruta!);
        Assert.Equal(3, lineas.Count);
        Assert.Contains("error,4xx", lineas[1]);
        Assert.Contains("item_perdido,,", lineas[2]);   // las pérdidas van sin tipo
    }

    [Fact]
    public void ComponerFila_TimestampInvariantYColumnas()
    {
        var fila = ErrorReportWriter.ComponerFila(Evento("obs rechazada"));
        Assert.Equal("2026-07-17 10:30:05,LabOrderSeeder,error,4xx,\"obs rechazada\"", fila);
    }

    [Fact]
    public void ComponerFila_EscapaComillasYAplanaSaltosDeLinea()
    {
        // El cuerpo de error de OpenMRS trae comillas y saltos de línea: la fila debe seguir siendo UNA
        var fila = ErrorReportWriter.ComponerFila(Evento("línea \"uno\"\r\nlínea dos\nlínea tres"));

        Assert.DoesNotContain('\n', fila);
        Assert.DoesNotContain('\r', fila);
        Assert.EndsWith("\"línea \"\"uno\"\" | línea dos | línea tres\"", fila);
    }

    [Fact]
    public void ComponerFila_NoRecortaMensajesLargos()
    {
        // El recorte de 220 es solo para los mensajes en memoria del resumen; el CSV lleva el completo
        var fila = ErrorReportWriter.ComponerFila(Evento(new string('x', 2000)));
        Assert.Contains(new string('x', 2000), fila);
    }

    private static List<string> LeerLineas(string ruta)
    {
        // FileShare.ReadWrite también al leer: el writer mantiene el fichero abierto
        using var stream = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lineas = new List<string>();
        while (reader.ReadLine() is { } linea) lineas.Add(linea);
        return lineas;
    }
}
