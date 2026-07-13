using Microsoft.Extensions.Logging;
using MySqlConnector;
using OpenmrsSeeder.Configuration;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Corrige las FECHAS DE AUDITORÍA (date_created y compañía) de los datos simulados.
///
/// El simulador siembra solo por REST, y OpenMRS sella cada fila con el reloj real del servidor. Por eso
/// las fechas de negocio (visita, obs, orden) son las simuladas —el seeder las manda explícitamente— pero
/// las de auditoría quedan todas el día de la corrida: para un ETL que ingiera "por fecha de inserción",
/// años de historia clínica ocurrieron en una sola tarde.
///
/// Esta clase es la ÚNICA vía no-REST del proyecto. No contiene los UPDATE: la lógica vive en los stored
/// procedures de <c>querys/sp_fechas_auditoria.sql</c> (versionado, revisable y ejecutable también a mano
/// con el cliente de MariaDB). Aquí solo se instalan, se llaman en orden y se vuelca su log a la consola.
///
/// El proceso es idempotente (cada UPDATE recalcula el destino desde la fecha de negocio, no desde el
/// valor actual), acotado a los pacientes con el prefijo configurado, y reversible (snapshot en
/// <c>sim_fecha_backup</c>).
/// </summary>
public class AuditDateFixer(
    OpenMrsSettings omrs,
    SimulationSettings sim,
    ILogger<AuditDateFixer> logger)
{
    private readonly DatabaseSettings _db = omrs.Database;

    /// <summary>Los UPDATE grandes (obs son ~170k filas) no caben en el timeout por defecto.</summary>
    private const int TimeoutSegundos = 3600;

    private long _ultimoLog;

    /// <summary>
    /// Minutos que el registro del paciente precede a su primera visita (pasa por recepción y luego entra
    /// a consulta). Determinista por paciente: el mismo cálculo que hace el SP, replicado aquí para poder
    /// fijar el contrato en un test.
    /// </summary>
    public static int DesfaseRegistroMinutos(int patientId) => 5 + patientId * 7 % 16;

    /// <summary>
    /// Ejecuta el proceso completo. Devuelve el número de filas corregidas (0 = ya estaba todo correcto,
    /// -1 = cancelado por el usuario en la confirmación).
    /// </summary>
    /// <param name="soloDryRun">true = informar y no escribir nada.</param>
    /// <param name="confirmar">Se invoca antes de escribir. Null = no preguntar.</param>
    public async Task<long> EjecutarAsync(bool soloDryRun, Func<long, bool>? confirmar, CancellationToken ct)
    {
        var ejecucion = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        // Los SP usan variables de usuario (@sql) para el SQL dinámico del aplicador. Sin esta opción el
        // driver las confunde con parámetros suyos y falla ya al instalarlos ("Parameter '@sql' must be
        // defined"). Se fuerza aquí para que la cadena de conexión de appsettings no tenga que saberlo.
        var conexion = new MySqlConnectionStringBuilder(_db.ConnectionString) { AllowUserVariables = true };

        await using var cn = new MySqlConnection(conexion.ConnectionString);
        await cn.OpenAsync(ct);
        logger.LogInformation("Conectado a MariaDB ({Servidor}) | ejecución {Ejecucion}", cn.DataSource, ejecucion);

        await InstalarProcedimientosAsync(cn, ct);

        // ── Preparar: calcular el destino de cada fila (no escribe en OpenMRS) ──────────────────────
        var barrido      = sim.EndDate.Date.AddDays(1).AddMinutes(-1);   // cierre de la ventana (23:59)
        var fechaMedicos = sim.StartDate.Date.AddHours(7);               // los médicos existen antes del día 1

        var alcance = await LeerConteosAsync(cn,
            "CALL sp_sim_fechas_preparar(@pre, @tol, @bar, @med, @eje)",
            ct,
            ("@pre", _db.PrefijoPaciente),
            ("@tol", sim.Appointments.ToleranciaDias),
            ("@bar", barrido),
            ("@med", fechaMedicos),
            ("@eje", ejecucion));

        await VolcarLogAsync(cn, ejecucion, ct);
        logger.LogInformation("Alcance: {Filas} filas de {Tablas} tablas (pacientes con identificador {Prefijo}*)",
            alcance.Sum(a => a.Filas), alcance.Count, _db.PrefijoPaciente);

        // ── Dry-run: cuántas filas cambiarían, sin escribir ─────────────────────────────────────────
        var previsto = await LeerConteosAsync(cn,
            "CALL sp_sim_fechas_aplicar(1, @lote, @eje)", ct,
            ("@lote", _db.TamanoLote), ("@eje", ejecucion));
        _ = await ConsumirLogAsync(cn, ejecucion, ct);   // el detalle por lote del dry-run no aporta

        var totalPrevisto = previsto.Sum(p => p.Filas);
        if (totalPrevisto == 0)
        {
            logger.LogInformation("Las fechas de auditoría ya son coherentes: 0 filas por corregir.");
            return 0;
        }

        logger.LogInformation("Filas por corregir: {Total} en {Tablas} tablas", totalPrevisto, previsto.Count);
        foreach (var (tabla, filas) in previsto)
            logger.LogInformation("   {Tabla,-30} {Filas,8}", tabla, filas);

        if (soloDryRun)
        {
            logger.LogInformation("Dry-run: no se ha escrito nada. Ejecuta sin --dry-run para aplicar.");
            return 0;
        }

        if (confirmar is not null && !confirmar(totalPrevisto))
        {
            logger.LogWarning("Corrección cancelada — no se tocó ningún dato.");
            return -1;
        }

        // ── Aplicar ─────────────────────────────────────────────────────────────────────────────────
        logger.LogInformation("Aplicando por lotes de {Lote} filas (snapshot reversible en sim_fecha_backup)…",
            _db.TamanoLote);
        var aplicado = await LeerConteosAsync(cn,
            "CALL sp_sim_fechas_aplicar(0, @lote, @eje)", ct,
            ("@lote", _db.TamanoLote), ("@eje", ejecucion));
        await VolcarLogAsync(cn, ejecucion, ct);

        var totalAplicado = aplicado.Sum(a => a.Filas);
        logger.LogInformation("Corregidas {Total} filas.", totalAplicado);

        // ── Verificar ───────────────────────────────────────────────────────────────────────────────
        await VerificarAsync(cn, ejecucion, ct);

        logger.LogInformation(
            "Reinicia el backend para vaciar la caché de Hibernate: `docker compose restart backend` " +
            "(y `restart gateway` si responde 502). Para deshacer: CALL sp_sim_fechas_revertir('{Ejecucion}');",
            ejecucion);

        return totalAplicado;
    }

    /// <summary>Instala (o reinstala) los stored procedures desde el .sql versionado.</summary>
    private async Task InstalarProcedimientosAsync(MySqlConnection cn, CancellationToken ct)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "sql", "sp_fechas_auditoria.sql");
        if (!File.Exists(ruta))
            throw new FileNotFoundException(
                $"No se encuentra el script de los stored procedures en {ruta} " +
                "(debería copiarlo el csproj desde querys/sp_fechas_auditoria.sql).");

        var sentencias = SqlScriptSplitter.Split(await File.ReadAllTextAsync(ruta, ct));
        foreach (var sentencia in sentencias)
        {
            await using var cmd = new MySqlCommand(sentencia, cn) { CommandTimeout = TimeoutSegundos };
            await cmd.ExecuteNonQueryAsync(ct);
        }
        logger.LogInformation("Stored procedures instalados ({N} sentencias desde querys/sp_fechas_auditoria.sql).",
            sentencias.Count);
    }

    /// <summary>Ejecuta un CALL cuyo result set son pares (tabla, filas).</summary>
    private static async Task<List<(string Tabla, long Filas)>> LeerConteosAsync(
        MySqlConnection cn, string sql, CancellationToken ct, params (string Nombre, object Valor)[] parametros)
    {
        await using var cmd = new MySqlCommand(sql, cn) { CommandTimeout = TimeoutSegundos };
        foreach (var (nombre, valor) in parametros)
            cmd.Parameters.AddWithValue(nombre, valor);

        var filas = new List<(string, long)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        do
        {
            while (await reader.ReadAsync(ct))
            {
                var tabla = reader["tabla"] as string ?? "";
                var n     = Convert.ToInt64(reader["filas"]);
                filas.Add((tabla, n));
            }
        } while (await reader.NextResultAsync(ct));

        return filas;
    }

    /// <summary>Lee las filas nuevas del log persistente y las devuelve (sin imprimirlas).</summary>
    private async Task<List<string>> ConsumirLogAsync(MySqlConnection cn, string ejecucion, CancellationToken ct)
    {
        const string sql = """
            SELECT id, fase, tabla, filas, mensaje
              FROM sim_fecha_log
             WHERE ejecucion = @eje AND id > @ultimo
             ORDER BY id
            """;
        await using var cmd = new MySqlCommand(sql, cn) { CommandTimeout = TimeoutSegundos };
        cmd.Parameters.AddWithValue("@eje", ejecucion);
        cmd.Parameters.AddWithValue("@ultimo", _ultimoLog);

        var lineas = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            _ultimoLog = reader.GetInt64("id");
            var fase    = reader.GetString("fase");
            var tabla   = reader["tabla"] as string;
            var filas   = reader["filas"] is DBNull ? (long?)null : Convert.ToInt64(reader["filas"]);
            var mensaje = reader["mensaje"] as string ?? "";
            lineas.Add(tabla is null
                ? $"[{fase}] {mensaje}"
                : $"[{fase}] {tabla,-30} {filas,8}  {mensaje}");
        }
        return lineas;
    }

    /// <summary>Vuelca a la consola el progreso que los SP han ido escribiendo en sim_fecha_log.</summary>
    private async Task VolcarLogAsync(MySqlConnection cn, string ejecucion, CancellationToken ct)
    {
        foreach (var linea in await ConsumirLogAsync(cn, ejecucion, ct))
            logger.LogInformation("   {Linea}", linea);
    }

    /// <summary>
    /// Informe final: por tabla, cuántas filas quedan pendientes (deben ser 0), cuántas caen fuera de la
    /// ventana simulada (deben ser 0) y el rango de fechas resultante.
    /// </summary>
    private async Task VerificarAsync(MySqlConnection cn, string ejecucion, CancellationToken ct)
    {
        var inicio = sim.StartDate.Date;
        var fin    = sim.EndDate.Date.AddDays(1).AddSeconds(-1);

        await using var cmd = new MySqlCommand(
            "CALL sp_sim_fechas_verificar(@ini, @fin, @eje)", cn) { CommandTimeout = TimeoutSegundos };
        cmd.Parameters.AddWithValue("@ini", inicio);
        cmd.Parameters.AddWithValue("@fin", fin);
        cmd.Parameters.AddWithValue("@eje", ejecucion);

        long pendientes = 0, fuera = 0;
        var lineas = new List<string>();

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            do
            {
                while (await reader.ReadAsync(ct))
                {
                    var tabla = reader.GetString("tabla");
                    var filas = Convert.ToInt64(reader["filas"]);
                    var pend  = Convert.ToInt64(reader["pendientes"]);
                    var fu    = Convert.ToInt64(reader["fuera_ventana"]);
                    var min   = reader["min_created"] as DateTime?;
                    var max   = reader["max_created"] as DateTime?;
                    pendientes += pend;
                    fuera      += fu;
                    lineas.Add($"{tabla,-30} {filas,8} filas | {min:yyyy-MM-dd} → {max:yyyy-MM-dd}" +
                               (pend + fu > 0 ? $" | ⚠ {pend} pendientes, {fu} fuera de ventana" : ""));
                }
            } while (await reader.NextResultAsync(ct));
        }

        logger.LogInformation("Verificación (ventana {Inicio:yyyy-MM-dd} → {Fin:yyyy-MM-dd}):", inicio, fin);
        foreach (var linea in lineas)
            logger.LogInformation("   {Linea}", linea);

        // Las coherencias fuertes las escriben los SP en el log
        await VolcarLogAsync(cn, ejecucion, ct);

        if (pendientes + fuera == 0)
            logger.LogInformation("Invariantes OK: 0 filas pendientes y 0 fuera de la ventana simulada.");
        else
            logger.LogError("Verificación FALLIDA: {Pendientes} filas pendientes y {Fuera} fuera de la ventana.",
                pendientes, fuera);
    }
}
