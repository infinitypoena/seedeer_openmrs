using System.Text;
using Microsoft.Extensions.Logging;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Vuelca a un fichero <b>todo lo que sale por consola</b>: las 5 etapas, el progreso, las advertencias y
/// los errores. Una corrida de 3,5 años escupe decenas de miles de líneas y la terminal no las guarda —
/// sin esto, auditar a mano los errores de una corrida larga es imposible.
///
/// <para>Se registra como un <see cref="ILoggerProvider"/> más (junto al de consola y al
/// <see cref="ErrorTallyLoggerProvider"/>), así que <b>captura a todo el mundo sin tocar a nadie</b>:
/// componentes futuros incluidos. Todo el informe del simulador pasa por <c>ILogger</c>, de modo que el
/// fichero es un espejo de la consola.</para>
///
/// <para>Dos detalles que importan en una corrida de horas:</para>
/// <list type="bullet">
/// <item><b>Flush línea a línea</b>: un Ctrl+C (o un cuelgue) deja el log completo hasta ese instante.</item>
/// <item><b><c>FileShare.ReadWrite</c></b>: el fichero se queda abierto toda la corrida y, con el share por
/// defecto, Windows no dejaría ni abrirlo para ir mirando cómo va (<c>Get-Content -Wait</c>).</item>
/// </list>
///
/// Si el fichero no se puede abrir, la corrida <b>sigue</b>: el log es evidencia, no la simulación.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter? _writer;
    private readonly object _candado = new();

    /// <summary>Ruta del log; <c>null</c> si no se pudo abrir o la feature está apagada.</summary>
    public string? Ruta { get; }

    /// <param name="carpeta">Carpeta de salida (<c>Simulation.Salida.Carpeta</c>); null/vacía = desactivado.</param>
    /// <param name="ahora">Marca de tiempo del nombre del fichero (inyectada: sin relojes ocultos).</param>
    public FileLoggerProvider(string? carpeta, DateTime ahora)
    {
        if (string.IsNullOrWhiteSpace(carpeta)) return;

        try
        {
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, $"corrida_{ahora:yyyy-MM-dd_HHmmss}.log");
            var stream = new FileStream(ruta, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            Ruta = ruta;
        }
        catch (Exception ex)
        {
            // Sin log en disco, pero la corrida continúa. Se avisa por consola (aún no hay logger).
            Console.Error.WriteLine($"[FileLogger] No se pudo abrir el log en '{carpeta}': {ex.Message}");
            _writer = null;
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        // Último segmento de la categoría, como el ErrorTally: "…Seeders.LabOrderSeeder" → "LabOrderSeeder".
        var fuente = categoryName.Contains('.')
            ? categoryName[(categoryName.LastIndexOf('.') + 1)..]
            : categoryName;
        return new ArchivoLogger(fuente, this);
    }

    /// <summary>Escribe una línea ya formateada. Serializado: el reporter de progreso corre en otro hilo.</summary>
    private void Escribir(string linea)
    {
        if (_writer is null) return;
        lock (_candado)
        {
            try { _writer.WriteLine(linea); }
            catch { /* disco lleno o fichero cerrado: la corrida no se cae por el log */ }
        }
    }

    public void Dispose()
    {
        lock (_candado)
        {
            _writer?.Dispose();
        }
    }

    private sealed class ArchivoLogger : ILogger
    {
        private readonly string _fuente;
        private readonly FileLoggerProvider _provider;

        public ArchivoLogger(string fuente, FileLoggerProvider provider)
        {
            _fuente   = fuente;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var linea = $"{DateTime.Now:HH:mm:ss} {Nivel(logLevel)} [{_fuente}] {formatter(state, exception)}";
            _provider.Escribir(linea);

            // La excepción completa (con stacktrace) solo va al fichero: es justo lo que se quiere poder
            // auditar después y lo que no cabe en la consola.
            if (exception is not null)
                _provider.Escribir(exception.ToString());
        }

        private static string Nivel(LogLevel nivel) => nivel switch
        {
            LogLevel.Trace       => "TRC",
            LogLevel.Debug       => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning     => "WRN",
            LogLevel.Error       => "ERR",
            LogLevel.Critical    => "CRT",
            _                    => "???"
        };
    }
}
