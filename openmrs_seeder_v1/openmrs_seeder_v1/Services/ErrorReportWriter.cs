using System.Globalization;
using System.Text;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Escribe <c>errores.csv</c> en la carpeta de salida: una fila por error de operación o ítem perdido,
/// con timestamp real, componente, tipo y el mensaje COMPLETO de OpenMRS (el único recorte es el de
/// 400 chars que ya aplica el cliente REST al cuerpo del error). Es la evidencia navegable: el
/// <c>.log</c> tiene los stacktraces pero mezclados con decenas de miles de líneas INFO, y el resumen
/// de consola trunca a 220 chars y capa 100 mensajes.
///
/// <para>Se crea al arrancar con solo la cabecera: un fichero vacío-de-filas es evidencia positiva de
/// "0 errores", y así captura también las etapas 1-2. Mismo credo que <see cref="FileLoggerProvider"/>:
/// flush línea a línea (un Ctrl+C deja la evidencia), <c>FileShare.ReadWrite</c> (se puede mirar en
/// caliente) y nunca lanza (el CSV es evidencia, no la simulación).</para>
/// </summary>
public sealed class ErrorReportWriter : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _candado = new();

    /// <summary>Ruta del CSV; <c>null</c> si la feature está apagada o el fichero no se pudo abrir.</summary>
    public string? Ruta { get; }

    /// <param name="carpeta">Carpeta de salida (<c>Simulation.Salida.Carpeta</c>); null/vacía = desactivado.</param>
    public ErrorReportWriter(string? carpeta)
    {
        if (string.IsNullOrWhiteSpace(carpeta)) return;

        try
        {
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, "errores.csv");
            var stream = new FileStream(ruta, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            _writer.WriteLine("timestamp,componente,evento,tipo,mensaje");
            Ruta = ruta;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorReport] No se pudo abrir errores.csv en '{carpeta}': {ex.Message}");
            _writer = null;
        }
    }

    /// <summary>Añade una fila. Serializado (los seeders y el reporter corren en hilos distintos); nunca lanza.</summary>
    public void Escribir(EventoDeError evento)
    {
        if (_writer is null) return;
        lock (_candado)
        {
            try { _writer.WriteLine(ComponerFila(evento)); }
            catch { /* disco lleno o fichero cerrado: la corrida no se cae por la evidencia */ }
        }
    }

    /// <summary>
    /// Seam puro: compone la fila CSV. El mensaje va entre comillas con las internas dobladas y los
    /// saltos de línea aplanados a " | " — una fila por evento, siempre (un CSV multilínea deja de ser
    /// navegable con head/grep).
    /// </summary>
    public static string ComponerFila(EventoDeError evento)
    {
        var timestamp = evento.Momento.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var tipoCsv   = evento.EsPerdida ? "" : ClasificadorErrores.Codigo(evento.Tipo);
        var eventoCsv = evento.EsPerdida ? "item_perdido" : "error";
        var mensaje = evento.Mensaje
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", " | ")
            .Replace("\"", "\"\"");
        return $"{timestamp},{evento.Fuente},{eventoCsv},{tipoCsv},\"{mensaje}\"";
    }

    public void Dispose()
    {
        lock (_candado)
        {
            _writer?.Dispose();
        }
    }
}
