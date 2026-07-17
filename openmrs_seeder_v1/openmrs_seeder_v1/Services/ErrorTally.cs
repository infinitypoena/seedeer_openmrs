using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OpenmrsSeeder.Services;

/// <summary>
/// EventIds del convenio de logging del simulador.
/// <para><b>ItemPerdido</b>: marca un <c>LogWarning</c> que significa que un dato que debió sembrarse
/// NO quedó en OpenMRS (una obs saltada por encounter ausente, una respuesta sin uuid…). Los cuenta
/// <see cref="ErrorTally"/> y van a <c>errores.csv</c>. Es opt-in a propósito: contar todos los Warning
/// contaría también los informativos ("Visita activa reutilizada", catálogo opcional vacío…).
/// <b>Regla para seeders futuros: si tu warning significa que un dato no quedó sembrado, márcalo con
/// este EventId</b> — y márcalo donde se pierde el dato concreto, no en los ecos del orquestador
/// (cada dato perdido cuenta una vez aunque varias pérdidas compartan causa raíz).</para>
/// </summary>
public static class Eventos
{
    public static readonly EventId ItemPerdido = new(1001, "ItemPerdido");
}

/// <summary>
/// Un error de operación o un ítem perdido, ya clasificado. Es lo que recibe el sink de
/// <see cref="ErrorTally"/> (el CSV de errores) — con el mensaje COMPLETO, sin el recorte de los
/// mensajes en memoria del resumen.
/// </summary>
public sealed record EventoDeError(DateTime Momento, string Fuente, TipoError Tipo, bool EsPerdida, string Mensaje);

/// <summary>
/// Contador preciso de errores de operación. Los seeders capturan sus fallos por-ítem (una obs
/// rechazada, una orden fallida…) y solo hacían LogError — el resumen final decía "0 errores"
/// aunque se hubieran perdido resultados. En vez de inyectar un colector en cada seeder, el
/// <see cref="ErrorTallyLoggerProvider"/> se registra como proveedor de logging y contabiliza aquí
/// cada evento de nivel Error por componente (también cubre seeders futuros sin tocarlos).
/// <para>Dos medidas distintas que no se suman: los <b>errores</b> (una escritura REST que falló,
/// clasificados por <see cref="TipoError"/>) y los <b>ítems perdidos</b> (un dato que debió sembrarse
/// y no quedó, marcados con <see cref="Eventos.ItemPerdido"/>). Un encounter que falla es 1 error,
/// pero puede arrastrar varias pérdidas aguas abajo.</para>
/// </summary>
public class ErrorTally
{
    private readonly ConcurrentDictionary<string, int> _porFuente = new();
    private readonly ConcurrentDictionary<(string Fuente, TipoError Tipo), int> _porFuenteYTipo = new();
    private readonly ConcurrentDictionary<string, int> _perdidasPorFuente = new();
    private readonly ConcurrentQueue<string> _mensajes = new();
    private readonly Func<DateTime> _reloj;
    private int _total;
    private int _totalPerdidas;
    private const int MaxMensajes = 100;
    private const int MaxLargoMensaje = 220;

    /// <param name="reloj">Seam de reloj para los timestamps de los eventos (default: DateTime.Now).</param>
    public ErrorTally(Func<DateTime>? reloj = null) => _reloj = reloj ?? (() => DateTime.Now);

    /// <summary>
    /// Receptor de cada evento (lo conecta Program al escritor de <c>errores.csv</c>). Recibe el mensaje
    /// completo, sin el recorte de 220 de los mensajes en memoria. Se invoca protegido: un fallo del sink
    /// (disco lleno) no tumba la corrida.
    /// </summary>
    public Action<EventoDeError>? Sink { get; set; }

    /// <summary>
    /// Cierre por cancelación (Ctrl+C): a partir de aquí no se contabilizan errores ni pérdidas, y el
    /// sink tampoco los recibe. Las POST en vuelo que se cancelan lanzan OperationCanceledException que
    /// los seeders registran como LogError; sin esto, un Ctrl+C a mitad de día inflaba el resumen (y el
    /// CSV) con una cascada de errores de operación falsos.
    /// </summary>
    private volatile bool _cancelando;
    public void MarcarCancelacion() => _cancelando = true;

    public void Registrar(string fuente, string mensaje, Exception? ex = null)
    {
        if (_cancelando) return;
        var tipo = ClasificadorErrores.Clasificar(ex);
        Interlocked.Increment(ref _total);
        _porFuente.AddOrUpdate(fuente, 1, (_, n) => n + 1);
        _porFuenteYTipo.AddOrUpdate((fuente, tipo), 1, (_, n) => n + 1);
        Encolar($"[{fuente}·{ClasificadorErrores.Codigo(tipo)}] ", mensaje);
        Emitir(new EventoDeError(_reloj(), fuente, tipo, EsPerdida: false, mensaje));
    }

    /// <summary>Un dato que debió sembrarse y no quedó (warning marcado con <see cref="Eventos.ItemPerdido"/>).</summary>
    public void RegistrarPerdida(string fuente, string mensaje)
    {
        if (_cancelando) return;
        Interlocked.Increment(ref _totalPerdidas);
        _perdidasPorFuente.AddOrUpdate(fuente, 1, (_, n) => n + 1);
        Encolar($"[{fuente}·perdido] ", mensaje);
        Emitir(new EventoDeError(_reloj(), fuente, TipoError.Otro, EsPerdida: true, mensaje));
    }

    private void Encolar(string prefijo, string mensaje)
    {
        if (_mensajes.Count >= MaxMensajes) return;
        // Los errores REST de OpenMRS traen el stacktrace Java completo — se recorta para el resumen
        var recortado = mensaje.Length > MaxLargoMensaje ? mensaje[..MaxLargoMensaje] + "…" : mensaje;
        _mensajes.Enqueue(prefijo + recortado);
    }

    private void Emitir(EventoDeError evento)
    {
        try { Sink?.Invoke(evento); }
        catch { /* el CSV es evidencia, no la simulación: un disco lleno no tumba la corrida */ }
    }

    public void Reset()
    {
        _porFuente.Clear();
        _porFuenteYTipo.Clear();
        _perdidasPorFuente.Clear();
        _mensajes.Clear();
        _total = 0;
        _totalPerdidas = 0;
        _cancelando = false;
    }

    public int Total => _total;

    public int TotalPerdidas => _totalPerdidas;

    /// <summary>Errores + pérdidas: para saber si los mensajes retenidos se quedaron cortos (cap 100).</summary>
    public int EventosTotales => _total + _totalPerdidas;

    public IReadOnlyDictionary<string, int> PorFuente => _porFuente;

    public IReadOnlyDictionary<string, int> PerdidasPorFuente => _perdidasPorFuente;

    /// <summary>Desglose componente × tipo ordenado por conteo: [("LabOrderSeeder", DatoRechazado, 8), …].</summary>
    public IReadOnlyList<(string Fuente, TipoError Tipo, int N)> PorFuenteYTipo() =>
        _porFuenteYTipo
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.Fuente)
            .Select(kv => (kv.Key.Fuente, kv.Key.Tipo, kv.Value))
            .ToList();

    public IReadOnlyCollection<string> Mensajes => _mensajes;

    /// <summary>Desglose compacto para el resumen, p.ej. "LabOrderSeeder: 8, VitalsSeeder: 1".</summary>
    public string Desglose() =>
        string.Join(", ", _porFuente.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));

    /// <summary>Desglose de pérdidas para el resumen, p.ej. "VitalsSeeder: 5 · ConsultaSeeder: 4".</summary>
    public string DesglosePerdidas() =>
        string.Join(" · ", _perdidasPorFuente.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));
}

/// <summary>
/// Proveedor de logging que alimenta el <see cref="ErrorTally"/>: cada log de nivel Error o superior
/// se cuenta bajo el último segmento de la categoría (p.ej. "OpenmrsSeeder.Seeders.LabOrderSeeder"
/// → "LabOrderSeeder") clasificado por su excepción, y cada Warning marcado con
/// <see cref="Eventos.ItemPerdido"/> se cuenta como pérdida. Cualquier otro Warning se ignora.
/// No escribe nada a consola — de eso sigue encargándose el proveedor estándar.
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

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                _tally.Registrar(_fuente, formatter(state, exception), exception);
            else if (logLevel == LogLevel.Warning && eventId.Id == Eventos.ItemPerdido.Id)
                _tally.RegistrarPerdida(_fuente, formatter(state, exception));
        }
    }
}
