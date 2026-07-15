using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

/// <summary>Fila de <c>crecimiento_diario.csv</c>: la curva de la clínica tal como ocurrió, día a día.</summary>
/// <param name="Activos">Clientela actual: pacientes que visitaron dentro de la ventana de actividad (el A(d) de la fórmula).</param>
/// <param name="CaptadosTotal">Pacientes distintos que han pasado por la clínica alguna vez.</param>
/// <param name="Lambda">Altas esperadas hoy (el λ de Bass, antes del peso del día y del sorteo).</param>
/// <param name="MediaMovil">
/// Media diaria de lo <b>ATENDIDO</b> en el último mes de consulta. Comparar su valor final con el inicial
/// ES la medida del crecimiento.
/// <para>⚠️ Antes esta columna era <c>λ / fracción de nuevos</c>, o sea el volumen que el modelo
/// <i>pretendía</i>, no el que se sembró: cantaba 45/día mientras el aforo recortaba a la clínica, y por
/// eso nadie vio que llevaba dos años y medio contra el techo.</para>
/// </param>
/// <param name="Aforo">Lo que cabía hoy en la consulta (el objetivo escalado por el peso del día).</param>
/// <param name="NuevosRechazados">Altas que no se captaron porque la consulta estaba llena: el freno del crecimiento, medido.</param>
public readonly record struct DiaDeCrecimiento(
    DateOnly Fecha,
    int Activos,
    int CaptadosTotal,
    int Satisfechos,
    double Lambda,
    double MediaMovil,
    int Atendidos,
    int Nuevos,
    int Recurrentes,
    int Aforo,
    int NuevosRechazados,
    double CalificacionMedia,
    double Saturacion);

/// <summary>
/// Escribe los dos artefactos de salida de la corrida. Es la ÚNICA parte del simulador que escribe
/// ficheros (todo lo demás lee catálogos y habla REST con OpenMRS).
///
/// <list type="bullet">
/// <item><c>crecimiento_diario.csv</c> — se va escribiendo día a día (append + flush), así una corrida
/// cancelada con Ctrl+C deja igualmente la curva hasta donde llegó. Es lo que se grafica.</item>
/// <item><c>clientes_recurrentes.csv</c> — instantánea del pool al terminar: un paciente por fila con
/// sus calificaciones, si quedó satisfecho y cuándo vino por última vez.</item>
/// </list>
///
/// Carpeta vacía en la configuración = feature apagada (no se escribe nada). Decimales en cultura
/// invariante (punto), igual que el convenio que ya lee <see cref="CatalogLoader"/>.
/// </summary>
public sealed class RunReportWriter : IDisposable
{
    private const string ArchivoCrecimiento = "crecimiento_diario.csv";
    private const string ArchivoClientes    = "clientes_recurrentes.csv";

    private readonly SimulationSettings _settings;
    private readonly ILogger<RunReportWriter> _logger;

    private StreamWriter? _crecimiento;
    private bool _fallido;

    public RunReportWriter(SimulationSettings settings, ILogger<RunReportWriter> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    /// <summary>Carpeta absoluta de salida; <c>null</c> si la feature está apagada.</summary>
    public string? Carpeta =>
        string.IsNullOrWhiteSpace(_settings.Salida.Carpeta)
            ? null
            : Path.IsPathRooted(_settings.Salida.Carpeta)
                ? _settings.Salida.Carpeta
                : Path.Combine(AppContext.BaseDirectory, _settings.Salida.Carpeta);

    public bool Activo => Carpeta is not null && !_fallido;

    /// <summary>Empieza una corrida nueva: crea la carpeta y estrena el CSV de la curva con su cabecera.</summary>
    public void Iniciar()
    {
        _fallido = false;
        Cerrar();

        if (Carpeta is not { } carpeta) return;

        try
        {
            Directory.CreateDirectory(carpeta);
            // FileShare.ReadWrite: el fichero se queda abierto toda la corrida (horas), y con el share por
            // defecto Windows no dejaría ni abrirlo para mirar cómo va la curva mientras se siembra.
            var stream = new FileStream(
                Path.Combine(carpeta, ArchivoCrecimiento),
                FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _crecimiento = new StreamWriter(stream, new UTF8Encoding(false));
            _crecimiento.WriteLine(
                "fecha,activos_A,captados_total,satisfechos_activos_S,lambda_altas,media_movil_30d," +
                "atendidos,nuevos,recurrentes,pct_recurrentes,aforo,altas_no_captadas," +
                "calificacion_media_dia,saturacion,pct_mercado");
            _crecimiento.Flush();
        }
        catch (Exception ex)
        {
            // Los CSV son evidencia, no la corrida: si el disco falla, se avisa y se sigue sembrando.
            _fallido = true;
            _crecimiento = null;
            _logger.LogWarning("No se pudo abrir {Archivo} en {Carpeta}: {Msg} — la corrida continúa sin CSV de salida.",
                ArchivoCrecimiento, carpeta, ex.Message);
        }
    }

    /// <summary>Añade el día que acaba de simularse a la curva de crecimiento.</summary>
    public void RegistrarDia(DiaDeCrecimiento d)
    {
        if (_crecimiento is null) return;

        // Qué parte del área de influencia es paciente de la clínica AHORA (no desde siempre): es lo
        // que la fórmula de Bass consume, y lo que de verdad frena la captación.
        var pctMercado = _settings.Crecimiento.PoblacionCaptacion <= 0
            ? 0
            : 100.0 * d.Activos / _settings.Crecimiento.PoblacionCaptacion;

        // La fracción de recurrentes del día. Es la columna que delata si el panel MADURA (ley L4): tiene
        // que SUBIR con los meses. Con el cupo fijo salía plana en el 31 %, el primer día y el último.
        var pctRecurrentes = d.Atendidos <= 0 ? 0 : 100.0 * d.Recurrentes / d.Atendidos;

        try
        {
            _crecimiento.WriteLine(string.Join(',',
                d.Fecha.ToString("yyyy-MM-dd"),
                N(d.Activos), N(d.CaptadosTotal), N(d.Satisfechos), N(d.Lambda, 2), N(d.MediaMovil, 2),
                N(d.Atendidos), N(d.Nuevos), N(d.Recurrentes), N(pctRecurrentes, 1),
                N(d.Aforo), N(d.NuevosRechazados),
                N(d.CalificacionMedia, 2), N(d.Saturacion, 3), N(pctMercado, 2)));
            _crecimiento.Flush();
        }
        catch (Exception ex)
        {
            _fallido = true;
            _logger.LogWarning("Se dejó de escribir {Archivo}: {Msg}", ArchivoCrecimiento, ex.Message);
            Cerrar();
        }
    }

    /// <summary>
    /// Vuelca la instantánea del pool y cierra la corrida. Se llama también cuando se cancela con
    /// Ctrl+C: lo sembrado hasta ese momento merece su evidencia.
    /// </summary>
    public void Finalizar(IReadOnlyList<SimulatedPatient> pool, DateOnly fechaCierre)
    {
        Cerrar();
        if (Carpeta is not { } carpeta) return;

        try
        {
            Directory.CreateDirectory(carpeta);
            using var w = new StreamWriter(
                Path.Combine(carpeta, ArchivoClientes), append: false, new UTF8Encoding(false));
            w.WriteLine("identifier,uuid,visitas,calificaciones,promedio,satisfecho,activo," +
                        "ultima_visita,proxima_cita,cronicas");

            var cr = _settings.Crecimiento;
            foreach (var p in pool)
            {
                var activo = p.UltimaVisita is { } u &&
                             fechaCierre.DayNumber - u.DayNumber <= cr.VentanaActividadDias;
                w.WriteLine(string.Join(',',
                    p.Identifier,
                    p.OpenMrsUuid,
                    // Visitas de verdad, no p.Calificaciones.Count: las notas solo existen con la
                    // satisfacción activa, y con ella apagada el padrón decía que nadie había venido nunca.
                    N(p.Visitas),
                    string.Join('|', p.Calificaciones),
                    N(p.CalificacionPromedio, 2),
                    (!p.Insatisfecho && p.Calificaciones.Count > 0) ? "true" : "false",
                    activo ? "true" : "false",
                    p.UltimaVisita?.ToString("yyyy-MM-dd") ?? "",
                    p.ProximaCita?.ToString("yyyy-MM-dd") ?? "",
                    N(p.CronicasActivas.Count)));
            }
        }
        catch (Exception ex)
        {
            _fallido = true;
            _logger.LogWarning("No se pudo escribir {Archivo}: {Msg}", ArchivoClientes, ex.Message);
        }
    }

    private void Cerrar()
    {
        _crecimiento?.Dispose();
        _crecimiento = null;
    }

    public void Dispose() => Cerrar();

    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string N(double v, int decimales) =>
        Math.Round(v, decimales).ToString(CultureInfo.InvariantCulture);
}
