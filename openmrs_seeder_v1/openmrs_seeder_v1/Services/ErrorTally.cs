using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Contador preciso de errores de operación. Los seeders capturan sus fallos por-ítem (una obs
/// rechazada, una orden fallida…) y solo hacían LogError — el resumen final decía "0 errores"
/// aunque se hubieran perdido resultados. En vez de inyectar un colector en cada seeder, el
/// <see cref="ErrorTallyLoggerProvider"/> se registra como proveedor de logging y contabiliza aquí
/// cada evento de nivel Error por componente (también cubre seeders futuros sin tocarlos).
/// </summary>
public class ErrorTally
{
    private readonly ConcurrentDictionary<string, int> _porFuente = new();
    private readonly ConcurrentQueue<string> _mensajes = new();
    private const int MaxMensajes = 100;
    private const int MaxLargoMensaje = 220;

    /// <summary>
    /// Cierre por cancelación (Ctrl+C): a partir de aquí no se contabilizan errores. Las POST en vuelo
    /// que se cancelan lanzan OperationCanceledException que los seeders registran como LogError; sin
    /// esto, un Ctrl+C a mitad de día inflaba el resumen con una cascada de errores de operación falsos.
    /// </summary>
    private volatile bool _cancelando;
    public void MarcarCancelacion() => _cancelando = true;

    public void Registrar(string fuente, string mensaje)
    {
        if (_cancelando) return;
        _porFuente.AddOrUpdate(fuente, 1, (_, n) => n + 1);
        if (_mensajes.Count < MaxMensajes)
        {
            // Los errores REST de OpenMRS traen el stacktrace Java completo — se recorta para el resumen
            var recortado = mensaje.Length > MaxLargoMensaje ? mensaje[..MaxLargoMensaje] + "…" : mensaje;
            _mensajes.Enqueue($"[{fuente}] {recortado}");
        }
    }

    public void Reset()
    {
        _porFuente.Clear();
        _mensajes.Clear();
        _cancelando = false;
    }

    public int Total => _porFuente.Values.Sum();

    public IReadOnlyDictionary<string, int> PorFuente => _porFuente;

    public IReadOnlyCollection<string> Mensajes => _mensajes;

    /// <summary>Desglose compacto para el resumen, p.ej. "LabOrderSeeder: 8, VitalsSeeder: 1".</summary>
    public string Desglose() =>
        string.Join(", ", _porFuente.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));
}

/// <summary>
/// Proveedor de logging que alimenta el <see cref="ErrorTally"/>: cada log de nivel Error o superior
/// se cuenta bajo el último segmento de la categoría (p.ej. "OpenmrsSeeder.Seeders.LabOrderSeeder"
/// → "LabOrderSeeder"). No escribe nada a consola — de eso sigue encargándose el proveedor estándar.
/// </summary>
public sealed class ErrorTallyLoggerProvider : ILoggerProvider
{
    private readonly ErrorTally _tally;

    public ErrorTallyLoggerProvider(ErrorTally tally) => _tally = tally;

    public ILogger CreateLogger(string categoryName)
    {
        var fuente = categoryName.Contains('.') ? categoryName[(categoryName.LastIndexOf('.') + 1)..] : categoryName;
        return new TallyLogger(fuente, _tally);
    }

    public void Dispose() { }

    private sealed class TallyLogger : ILogger
    {
        private readonly string _fuente;
        private readonly ErrorTally _tally;

        public TallyLogger(string fuente, ErrorTally tally)
        {
            _fuente = fuente;
            _tally = tally;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error) return;
            _tally.Registrar(_fuente, formatter(state, exception));
        }
    }
}
