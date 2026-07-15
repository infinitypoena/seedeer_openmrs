using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;

// Margen entre el informe previo y la primera escritura en OpenMRS (Ctrl+C para abortar a tiempo).
const int PausaPreviaSegundos = 5;

// Simulador clínico OpenMRS — ejecución batch: `dotnet run` corre la simulación completa según
// appsettings.json y termina. `dotnet run -- clear` anula (void) todos los datos SIM- previos.
// `dotnet run -- fechas [--dry-run]` corrige las fechas de auditoría (date_created) de los datos ya
// sembrados; es también la etapa 5/5 de una corrida normal si OpenMRS:Database:CorregirFechas = true.
// Exit codes: 0 = corrida completada · 1 = fallo del proceso · 2 = OpenMRS inaccesible / uso inválido /
// catálogos inválidos (en los tres casos no se toca ningún dato) · 3 = la corrida terminó pero ROMPIÓ
// alguna ley de la simulación (los datos están sembrados y no describen una clínica coherente — ver
// Services/Invariantes.cs y leyes_simulacion.md).

// Content root = carpeta del binario (allí se copian appsettings.json y catalogs/), así la app
// funciona igual desde cualquier directorio de trabajo (dotnet run, exe publicado, Docker).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Settings
var omrsSettings = builder.Configuration.GetSection("OpenMRS").Get<OpenMrsSettings>()
    ?? throw new InvalidOperationException("Falta la sección 'OpenMRS' en appsettings.json");
var simSettings = builder.Configuration.GetSection("Simulation").Get<SimulationSettings>()
    ?? throw new InvalidOperationException("Falta la sección 'Simulation' en appsettings.json");

// Fail-fast: no arrancar con configuración inválida (una probabilidad fuera de rango o una banda
// invertida solo se manifestaría como comportamiento raro a mitad de una corrida de horas).
var violaciones = SettingsValidator.Validate(simSettings, omrsSettings);
if (violaciones.Count > 0)
    throw new InvalidOperationException(
        "Configuración inválida en appsettings.json:\n - " + string.Join("\n - ", violaciones));

// Offset UTC de todas las fechas enviadas (lanza FormatException si es inválido — fail-fast)
VisitSeeder.UtcOffset = VisitSeeder.NormalizarOffset(simSettings.UtcOffset);

builder.Services.AddSingleton(omrsSettings);
builder.Services.AddSingleton(simSettings);

// Contador preciso de errores de operación: cada LogError de cualquier componente (obs rechazada,
// orden fallida…) se contabiliza por fuente, para que el resumen final no diga "0 errores" cuando
// en realidad se perdieron ítems.
var errorTally = new ErrorTally();
builder.Services.AddSingleton(errorTally);
builder.Logging.AddProvider(new ErrorTallyLoggerProvider(errorTally));

// Log en fichero: un espejo de la consola en output/corrida_<fecha>.log. Una corrida de años escupe
// decenas de miles de líneas y la terminal no las guarda — sin esto no hay forma de auditar los errores
// después. Se registra aquí para que capture ya la etapa 1/5 (la validación de catálogos).
var carpetaSalida = string.IsNullOrWhiteSpace(simSettings.Salida.Carpeta)
    ? null
    : Path.IsPathRooted(simSettings.Salida.Carpeta)
        ? simSettings.Salida.Carpeta
        : Path.Combine(AppContext.BaseDirectory, simSettings.Salida.Carpeta);

var fileLogger = new FileLoggerProvider(
    simSettings.Salida.ArchivoLog ? carpetaSalida : null, DateTime.Now);
builder.Logging.AddProvider(fileLogger);

// Servicios singleton (stateless, seguros para reusar)
builder.Services.AddSingleton<SeedProgressTracker>();
builder.Services.AddSingleton<RunStats>();
builder.Services.AddSingleton<CatalogLoader>();
// Los CSV de salida (la curva de crecimiento y el padrón de pacientes): singleton porque mantienen
// abierto el fichero de la curva durante toda la corrida.
builder.Services.AddSingleton<RunReportWriter>();
builder.Services.AddSingleton<DailyScheduleGenerator>();
builder.Services.AddSingleton<PatientProfileGenerator>();
builder.Services.AddSingleton<EpidemiologySelector>();
builder.Services.AddSingleton<ClimateResolver>();

// Asignador de consultorio/médico (transient: hace REST por corrida, como los seeders)
builder.Services.AddTransient<ClinicResourceAssigner>();
builder.Services.AddTransient<ProviderEnsurer>();

// El personal de laboratorio y el flujo de la orden son SINGLETON a propósito: los inyectan dos
// consumidores (el orquestador y LabOrderSeeder), y guardan estado de la corrida (el pool de técnicos
// que se asegura una sola vez, el correlativo del nº de muestra). Con transient, el orquestador
// inicializaría el personal en una instancia y el seeder usaría otra vacía.
builder.Services.AddSingleton<LabStaffAssigner>();
builder.Services.AddSingleton<LabWorkflowSeeder>();

// Seeders transient (dependen de HttpClient via DI)
builder.Services.AddTransient<PatientSeeder>();
builder.Services.AddTransient<AllergySeeder>();
builder.Services.AddTransient<VisitSeeder>();
builder.Services.AddTransient<VitalsSeeder>();
builder.Services.AddTransient<ConsultaSeeder>();
builder.Services.AddTransient<LabOrderSeeder>();
builder.Services.AddTransient<PrescriptionSeeder>();
builder.Services.AddTransient<VisitCloseSeeder>();
builder.Services.AddTransient<ConditionSeeder>();
builder.Services.AddTransient<ProgramEnrollmentSeeder>();
builder.Services.AddTransient<AppointmentSeeder>();
builder.Services.AddTransient<SeedOrchestrator>();
builder.Services.AddTransient<DataCleaner>();

// Única vía no-REST: retrofecha las fechas de auditoría que OpenMRS sella con su propio reloj
builder.Services.AddTransient<AuditDateFixer>();

// HttpClient para OpenMRS REST API con BasicAuth
builder.Services.AddHttpClient<OpenMrsRestClient>(client =>
{
    client.BaseAddress = new Uri(omrsSettings.RestApi.BaseUrl.TrimEnd('/') + "/");
    var credentials = Convert.ToBase64String(
        Encoding.ASCII.GetBytes($"{omrsSettings.RestApi.Username}:{omrsSettings.RestApi.Password}"));
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    client.Timeout = TimeSpan.FromSeconds(30);
});

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");

if (fileLogger.Ruta is { } rutaLog)
    logger.LogInformation("Log de esta corrida: {Ruta}", rutaLog);

// Claves del JSON que el binding ignoró en silencio (típicamente parámetros renombrados/obsoletos).
var clavesDesconocidas = SettingsValidator
    .FindUnknownKeys(builder.Configuration.GetSection("Simulation"), typeof(SimulationSettings))
    .Concat(SettingsValidator.FindUnknownKeys(builder.Configuration.GetSection("OpenMRS"), typeof(OpenMrsSettings)));
foreach (var clave in clavesDesconocidas)
    logger.LogWarning("Clave de configuración desconocida (ignorada por el binding): {Clave}", clave);

// ══ Etapa 1/5 · Catálogos ═══════════════════════════════════════════════════════════════════════
// Fail-fast: el loader es mudo (CSV ausente → lista vacía, booleano mal escrito → false), así que una
// errata se manifestaría como una feature apagada en silencio o como comportamiento raro a mitad de la
// corrida. Se carga, se informa de lo cargado y se valida ANTES de tocar OpenMRS.
var catalogLoader = host.Services.GetRequiredService<CatalogLoader>();
catalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalogs"));

logger.LogInformation("══ Etapa 1/5 · Validación de catálogos ══");
foreach (var (archivo, filas, opcional) in new (string, int, bool)[]
{
    ("epidemiology-profile.csv",     catalogLoader.EpidemiologyProfile.Count, false),
    ("diagnosticos.csv",             catalogLoader.Diagnosticos.Count,        false),
    ("medicamentos.csv",             catalogLoader.Medicamentos.Count,        false),
    ("laboratorios.csv",             catalogLoader.Laboratorios.Count,        false),
    ("paneles.csv",                  catalogLoader.Paneles.Count,             true),
    ("examenes_clinicos.csv",        catalogLoader.ExamenesClinicos.Count,    false),
    ("alergenos.csv",                catalogLoader.Alergenos.Count,           false),
    ("motivos_consulta.csv",         catalogLoader.MotivosConsulta.Count,     false),
    ("nombres.csv",                  catalogLoader.Nombres.Count,             false),
    ("apellidos.csv",                catalogLoader.Apellidos.Count,           false),
    ("direcciones.csv",              catalogLoader.Direcciones.Count,         true),
    ("consultorios.csv",             catalogLoader.Consultorios.Count,        true),
    ("personal_laboratorio.csv",     catalogLoader.PersonalLaboratorio.Count, true),
    ("comorbilidad_afinidades.csv",  catalogLoader.Afinidades.Count,          true),
    ("programas.csv",                catalogLoader.Programas.Count,           true),
    ("clima.csv",                    catalogLoader.Clima.Count,               true)
})
{
    var estado = filas > 0 ? $"{filas,5} filas" : opcional ? "    — (opcional, desactivado)" : "    VACÍO";
    logger.LogInformation("   {Archivo,-28} {Estado}", archivo, estado);
}

var (erroresCatalogo, avisosCatalogo) = CatalogValidator.Validate(catalogLoader);
foreach (var aviso in avisosCatalogo)
    logger.LogWarning("   ⚠ {Aviso}", aviso);

if (erroresCatalogo.Count > 0)
{
    logger.LogError("Catálogos INVÁLIDOS ({N} problema(s)) — se aborta sin tocar ningún dato:", erroresCatalogo.Count);
    foreach (var error in erroresCatalogo)
        logger.LogError("   - {Error}", error);
    return 2;
}
logger.LogInformation("Catálogos válidos ({A} advertencia(s), 0 errores).", avisosCatalogo.Count);

// Ctrl+C → cancelación limpia (los datos ya insertados persisten)
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.LogWarning("Cancelación solicitada (Ctrl+C) — cerrando de forma limpia…");
    // Dejar de contabilizar errores: las POST en vuelo que se cancelan no son fallos reales de operación.
    errorTally.MarcarCancelacion();
    cts.Cancel();
};

// Conexión con la instancia (el viejo GET /api/seed/status, ahora por consola)
var client = host.Services.GetRequiredService<OpenMrsRestClient>();
var online = await client.PingAsync(cts.Token);
logger.LogInformation(
    "OpenMRS: {Estado} ({BaseUrl}) | seed {Seed}",
    online ? "ONLINE" : "OFFLINE", omrsSettings.RestApi.BaseUrl, simSettings.RandomSeed);

if (!online)
{
    logger.LogError("OpenMRS no responde en {BaseUrl} — no se toca ningún dato. " +
        "Verificar que la instancia esté arriba y la URL/credenciales en appsettings.json.",
        omrsSettings.RestApi.BaseUrl);
    return 2;
}

var modo = args.FirstOrDefault()?.ToLowerInvariant() ?? "run";
switch (modo)
{
    case "run":
        return await EjecutarSimulacionAsync();
    case "clear":
        return await EjecutarLimpiezaAsync();
    case "fechas":
        var codigo = await CorregirFechasAsync(
            soloDryRun: args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)),
            automatico: false);
        EsperarTecla();
        return codigo;
    default:
        logger.LogError("Argumento no reconocido: '{Modo}'. Uso: dotnet run [-- run|clear|fechas [--dry-run]]", modo);
        return 2;
}

async Task<int> EjecutarSimulacionAsync()
{
    var tracker      = host.Services.GetRequiredService<SeedProgressTracker>();
    var orchestrator = host.Services.GetRequiredService<SeedOrchestrator>();

    // ══ Etapa 2/5 · Días a simular ═══════════════════════════════════════════════════════════════
    // El plan se genera aquí una sola vez y el orquestador reusa ESTE mismo (PlanificarDias lo cachea):
    // el informe describe la corrida que realmente se va a ejecutar, no una tirada distinta.
    var plan = orchestrator.PlanificarDias();
    ReportarPlan(plan);

    // ══ Etapa 3/5 · Ejecución ════════════════════════════════════════════════════════════════════
    // Margen para abortar (Ctrl+C) tras leer el informe: a partir de aquí se escribe en OpenMRS.
    logger.LogInformation("══ Etapa 3/5 · Ejecución — comenzando en {S} s (Ctrl+C para abortar) ══",
        PausaPreviaSegundos);
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(PausaPreviaSegundos), cts.Token);
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("Cancelado antes de empezar — no se tocó ningún dato.");
        return 0;
    }

    var cronometro = System.Diagnostics.Stopwatch.StartNew();
    var runId = tracker.CreateRun();
    tracker.Update(runId, r => { r.Etapa = "iniciando"; r.Porcentaje = 0; });
    errorTally.Reset();

    // Reporter: imprime el avance cada 15 s mientras la corrida está en curso
    using var reporterCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
    var reporter = Task.Run(async () =>
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(reporterCts.Token))
            {
                var r = tracker.GetRun(runId);
                if (r is null) continue;
                logger.LogInformation(
                    "Progreso: {Pct}% | día {Dias}/{Total} ({Fecha}) | {Pacientes} pacientes | {Errores} errores de proceso | {Operacion} errores de operación",
                    r.Porcentaje, r.DiasProcesados, r.TotalDias, r.FechaActual,
                    r.PacientesCreados, r.Errores.Count, errorTally.Total);
            }
        }
        catch (OperationCanceledException) { /* fin normal */ }
        catch (Exception ex)
        {
            // El reporter es cosmético: un fallo suyo no debe tumbar el cierre de la corrida
            logger.LogWarning("Reporter de progreso detenido por error: {Msg}", ex.Message);
        }
    });

    try
    {
        await orchestrator.RunAsync(runId, tracker, cts.Token);
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("Corrida cancelada por el usuario.");
    }
    catch (Exception ex)
    {
        tracker.Update(runId, r =>
        {
            r.Errores.Add($"Error crítico: {ex.Message}");
            r.Etapa      = "error";
            r.Completado = true;
        });
    }
    finally
    {
        reporterCts.Cancel();
        await reporter;
    }

    // ══ Etapa 4/5 · Resumen final ════════════════════════════════════════════════════════════════
    var run   = tracker.GetRun(runId)!;
    var stats = host.Services.GetRequiredService<RunStats>();

    logger.LogInformation("══ Etapa 4/5 · Resumen final ══");
    logger.LogInformation(
        "Etapa '{Etapa}' | {Dias}/{Total} días simulados | ventana {Inicio:yyyy-MM-dd} → {Fin:yyyy-MM-dd} | " +
        "duración {Duracion:hh\\:mm\\:ss}",
        run.Etapa, run.DiasProcesados, run.TotalDias,
        simSettings.StartDate, simSettings.EndDate, cronometro.Elapsed);
    logger.LogInformation(
        "Visitas: {Total} ({Nuevos} de pacientes nuevos + {Rec} de recurrentes)",
        stats.TotalVisitas, stats.VisitasDeNuevos, stats.VisitasDeRecurrentes);
    logger.LogInformation(
        "Pacientes: {Unicos} distintos | {Volvieron} volvieron alguna vez ({PctVolvieron:0.0} %) | " +
        "media {Media:0.00} visitas/paciente",
        stats.PacientesUnicos, stats.PacientesQueVolvieron,
        stats.PacientesUnicos == 0 ? 0 : 100.0 * stats.PacientesQueVolvieron / stats.PacientesUnicos,
        stats.VisitasPorPaciente);

    var semanas = stats.PorSemana();
    if (semanas.Count > 0)
    {
        logger.LogInformation("Visitas por semana ({N} semanas, la fecha es el lunes que la abre):", semanas.Count);
        foreach (var (semana, totalSemana, nuevosSemana, recSemana) in semanas)
            logger.LogInformation("   semana del {Semana:yyyy-MM-dd}  {Total,4} visitas ({Nuevos} nuevos, {Rec} recurrentes)",
                semana, totalSemana, nuevosSemana, recSemana);
    }

    // ── Cómo maduró el panel (lo que el cupo fijo ocultaba) ───────────────────────────────────────
    var anios = stats.PorAnio();
    if (anios.Count > 0)
    {
        logger.LogInformation("Panel de pacientes por año (la fracción de recurrentes tiene que SUBIR):");
        foreach (var (anio, totalAnio, nuevosAnio, recAnio) in anios)
            logger.LogInformation(
                "   {Anio}  {Total,6} visitas ({Nuevos} altas, {Rec} controles = {Pct,4:0.0} % recurrentes)",
                anio, totalAnio, nuevosAnio, recAnio, totalAnio == 0 ? 0 : 100.0 * recAnio / totalAnio);
    }

    logger.LogInformation(
        "Agenda: {Cumplidas} citas cumplidas, {Perdidas} perdidas ({PctPerdidas:0.0} % de no-show)",
        stats.CitasCompletadas, stats.CitasPerdidas, 100 * stats.FraccionCitasPerdidas);

    // ── Crecimiento y satisfacción ────────────────────────────────────────────────────────────────
    if (simSettings.Crecimiento.Enabled || simSettings.Satisfaccion.Enabled)
    {
        var cr = simSettings.Crecimiento;
        var pctMercado = cr.PoblacionCaptacion == 0
            ? 0
            : 100.0 * stats.PacientesActivosFinal / cr.PoblacionCaptacion;

        // Se reporta el PICO además del final: si el área se llena, la captación decae y la media final
        // baja del pico — con solo el último mes parecería que la clínica encogió cuando en realidad creció
        // hasta topar con su mercado. Y son medias de lo ATENDIDO, no de lo que el modelo pretendía.
        logger.LogInformation(
            "Crecimiento: media diaria {Inicial:0.0} → máx. {Maxima:0.0} → {Final:0.0} visitas/día " +
            "(objetivo: {Objetivo}) | {Captados} pacientes captados en total, {Activos} siguen viniendo = " +
            "{PctMercado:0.0} % de un área de {M} | {S} recurrentes satisfechos activos (el boca a boca " +
            "que la sostiene) | {Rechazados} altas no captadas por consulta llena",
            stats.MediaDiariaInicial, stats.MediaDiariaMaxima, stats.MediaDiariaFinal,
            cr.Enabled ? cr.PacientesPorDiaObjetivo.ToString() : "—",
            stats.PacientesUnicos, stats.PacientesActivosFinal, pctMercado,
            cr.PoblacionCaptacion, stats.RecurrentesSatisfechosFinal, stats.NuevosRechazadosPorAforo);

        if (cr.Enabled && pctMercado > 80)
            logger.LogWarning(
                "El área de captación se está agotando ({Pct:0.0} % ya es clientela): la llegada de " +
                "pacientes nuevos se frena y la media diaria cae desde su pico. Si no es lo que buscas, " +
                "sube Crecimiento.PoblacionCaptacion.", pctMercado);

        if (stats.VisitasCalificadas > 0)
        {
            logger.LogInformation("Satisfacción: nota media {Media:0.00}/5 sobre {N} visitas calificadas",
                stats.CalificacionMedia, stats.VisitasCalificadas);
            foreach (var (nota, veces) in stats.Histograma())
                logger.LogInformation("   {Nota} ★  {Veces,6} visitas ({Pct,5:0.0} %)  {Barra}",
                    nota, veces, 100.0 * veces / stats.VisitasCalificadas,
                    new string('█', (int)Math.Round(40.0 * veces / stats.VisitasCalificadas)));
        }

        var writer = host.Services.GetRequiredService<RunReportWriter>();
        if (writer.Carpeta is { } carpetaEvidencia)
            logger.LogInformation("Evidencia de la corrida (curva de crecimiento + padrón de pacientes): {Carpeta}",
                carpetaEvidencia);
    }

    var top = stats.TopDiagnosticos(5);
    if (top.Count > 0)
    {
        logger.LogInformation(
            "Top 5 diagnósticos de {Distintos} distintos (primarios + comorbilidades; el % es sobre las visitas):",
            stats.DiagnosticosDistintos);
        var puesto = 1;
        foreach (var (nombre, veces, pct) in top)
            logger.LogInformation("   {Puesto}. {Nombre,-45} {Veces,4} veces ({Pct:0.0} %)", puesto++, nombre, veces, pct);
    }

    logger.LogInformation("Errores: {Proceso} de proceso | {Operacion} de operación",
        run.Errores.Count, errorTally.Total);
    foreach (var error in run.Errores)
        logger.LogWarning("  [proceso] {Error}", error);
    if (errorTally.Total > 0)
    {
        logger.LogWarning("Errores de operación por componente: {Desglose}", errorTally.Desglose());
        foreach (var mensaje in errorTally.Mensajes)
            logger.LogWarning("  {Mensaje}", mensaje);
    }

    // ── Las leyes de la simulación ────────────────────────────────────────────────────────────────
    // El veredicto sobre lo que de VERDAD se sembró. Una corrida puede terminar sin un solo error y aun
    // así no describir una clínica: eso fue exactamente lo que pasó con la de 3,5 años. Ver
    // leyes_simulacion.md. Una ley rota no revierte nada (los datos ya están), pero cambia el exit code:
    // la corrida no se declara buena.
    var leyes = Invariantes.Evaluar(stats, orchestrator.Pool, simSettings);
    ReportarLeyes(leyes, "Leyes de la simulación", rotasSonError: true);
    var leyesRotas = Invariantes.Rotas(leyes);
    if (leyesRotas.Count > 0)
        logger.LogError(
            "La corrida termina con {N} ley(es) de la simulación ROTA(S) ({Codigos}): los datos están " +
            "sembrados, pero NO describen una clínica coherente. No los des por buenos.",
            leyesRotas.Count, string.Join(", ", leyesRotas.Select(l => l.Codigo)));
    else
        logger.LogInformation("Leyes de la simulación: todas las aplicables se cumplen.");

    if (fileLogger.Ruta is { } rutaFinal)
        logger.LogInformation("Log completo de la corrida: {Ruta}", rutaFinal);

    // ══ Etapa 5/5 · Fechas de auditoría ══════════════════════════════════════════════════════════
    var codigoFechas = await CorregirFechasAsync(soloDryRun: false, automatico: true);

    EsperarTecla();
    if (run.Etapa == "error") return 1;
    if (codigoFechas != 0) return codigoFechas;
    return leyesRotas.Count > 0 ? 3 : 0;
}

/// <summary>
/// Etapa 5/5 · Retrofecha las fechas de auditoría (date_created y compañía) de los datos SIM-.
///
/// El seeder escribe solo por REST y OpenMRS sella cada fila con SU reloj: las fechas de negocio (visita,
/// obs, orden) son las simuladas, pero las de auditoría quedan todas el día de la corrida. Este paso las
/// deriva de la fecha de negocio, es idempotente y solo toca a los pacientes del prefijo configurado.
/// Toda la lógica SQL vive en los stored procedures de querys/sp_fechas_auditoria.sql.
///
/// Es la única parte del simulador que habla con MariaDB, y está desactivada por defecto: sin
/// OpenMRS:Database:CorregirFechas = true (y una cadena de conexión) el proyecto sigue siendo REST puro.
/// </summary>
async Task<int> CorregirFechasAsync(bool soloDryRun, bool automatico)
{
    var db = omrsSettings.Database;

    if (automatico)
        logger.LogInformation("══ Etapa 5/5 · Fechas de auditoría ══");

    if (!db.Activo)
    {
        var motivo = db.CorregirFechas
            ? "OpenMRS:Database:ConnectionString está vacío"
            : "OpenMRS:Database:CorregirFechas = false";
        if (!automatico)
        {
            logger.LogError("La corrección de fechas está desactivada ({Motivo}). Actívala en appsettings.json.", motivo);
            return 2;
        }
        logger.LogInformation(
            "Desactivada ({Motivo}) — las fechas de auditoría se quedan con la fecha de esta corrida.", motivo);
        return 0;
    }

    if (cts.IsCancellationRequested)
    {
        logger.LogWarning("Corrida cancelada: no se tocan las fechas. Ejecuta `dotnet run -- fechas` cuando quieras.");
        return 0;
    }

    try
    {
        await host.Services.GetRequiredService<AuditDateFixer>()
            .EjecutarAsync(soloDryRun, ConfirmarCorreccionFechas, cts.Token);
        return 0;
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("Corrección cancelada. El proceso es idempotente: re-ejecútalo con `dotnet run -- fechas`.");
        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError("No se pudieron corregir las fechas de auditoría: {Mensaje}", ex.Message);
        return 1;
    }
}

/// <summary>Confirmación previa a escribir (la misma fricción que `clear`). false = no aplicar.</summary>
bool ConfirmarCorreccionFechas(long filas)
{
    var db = omrsSettings.Database;
    if (!db.PedirConfirmacion) return true;

    if (Console.IsInputRedirected)
    {
        logger.LogWarning(
            "Entrada no interactiva: no se puede confirmar. Pon OpenMRS:Database:PedirConfirmacion = false " +
            "para aplicar sin preguntar.");
        return false;
    }

    logger.LogWarning("Conviene tener un backup reciente antes de continuar (scripts/backup_openmrs.ps1).");
    Console.Write($"Se corregirán las fechas de auditoría de {filas} filas de pacientes " +
                  $"{db.PrefijoPaciente}*. ¿Continuar? (s/N): ");
    var respuesta = Console.ReadLine()?.Trim().ToLowerInvariant();
    return respuesta is "s" or "si" or "sí";
}

/// <summary>
/// Deja el resumen final en pantalla hasta que el usuario pulse una tecla (si se ejecuta a doble clic
/// o desde una terminal que se cierra al terminar, si no no habría tiempo de leerlo). Se omite si la
/// entrada no es interactiva (Docker, CI, salida redirigida a un fichero) — ahí colgaría el proceso — o
/// si la corrida se canceló con Ctrl+C.
/// </summary>
void EsperarTecla()
{
    if (cts.IsCancellationRequested) return;
    if (Console.IsInputRedirected)
    {
        logger.LogInformation("Fin (entrada no interactiva: no se espera tecla).");
        return;
    }

    logger.LogInformation("Pulsa cualquier tecla para finalizar…");
    try { Console.ReadKey(intercept: true); }
    catch (InvalidOperationException) { /* sin consola asociada: terminar sin esperar */ }
}

/// <summary>
/// Informe de los días que se van a simular: ventana, volumen previsto y desglose. Con pocos días el
/// desglose es diario; con muchos (corridas de meses o años) se agrupa por mes para no inundar la
/// consola.
///
/// <para>Sin crecimiento, los volúmenes ya vienen sorteados (peso del día + normal): es exactamente lo
/// que se va a sembrar. <b>Con crecimiento activo el volumen no se puede precalcular</b> — depende de
/// cuántos pacientes satisfechos vaya acumulando la clínica —, así que se muestra la <b>proyección
/// determinista</b> de la curva de Bass: una estimación, no una predicción. La curva real la escribe la
/// corrida en <c>crecimiento_diario.csv</c>.</para>
/// </summary>
void ReportarPlan(IReadOnlyList<DailySchedule> plan)
{
    var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
    var cr = simSettings.Crecimiento;

    logger.LogInformation("══ Etapa 2/5 · Días a simular ══");

    // La proyección describe los dos modos: con crecimiento las altas las pide Bass, sin él salen del plan
    // precalculado — pero los RETORNOS los estima igual, porque en ambos modos los pone el panel. El boca a
    // boca se deriva UNA vez (la bisección recorre la ventana entera) y se reusa en todo el informe.
    var qBocaABoca = cr.Enabled ? BassGrowthModel.CoeficienteImitacion(plan, simSettings) : 0;
    var proyeccion = BassGrowthModel.Proyectar(plan, simSettings, qBocaABoca);

    var dias = proyeccion.Where(d => d.Total > 0)
        .Select(d => (Fecha: d.Fecha, Total: d.Total, Nuevos: d.Nuevos, Recurrentes: d.Recurrentes))
        .ToList();

    var total       = dias.Sum(d => d.Total);
    var nuevos      = dias.Sum(d => d.Nuevos);
    var recurrentes = dias.Sum(d => d.Recurrentes);

    logger.LogInformation(
        "Ventana: {Inicio:yyyy-MM-dd} → {Fin:yyyy-MM-dd} | {Dias} días naturales, {ConAtencion} con " +
        "atención ({Cerrados} cerrados por peso 0 en WeekdayWeights)",
        simSettings.StartDate, simSettings.EndDate,
        plan.Count, dias.Count, plan.Count - dias.Count);

    if (dias.Count == 0)
    {
        logger.LogWarning("Ningún día de la ventana tiene pacientes — revisa WeekdayWeights y las fechas.");
        return;
    }

    // Cuánta consulta genera el panel POR SÍ SOLO: cada paciente captado vuelve 1/(1−k) veces. Es el
    // "suelo" de la clínica y el número que explica por qué el volumen ya no se puede derivar de las altas.
    var k = BassGrowthModel.ProbabilidadDeRetorno(simSettings);
    logger.LogInformation(
        "El panel genera su propia consulta: {K:P0} de las visitas dejan otra visita del mismo paciente " +
        "(cita de control + retorno espontáneo) → cada paciente vuelve {V:0.0} veces, y esa misma fracción " +
        "es la de recurrentes en régimen. Las altas ({Medio}/día al arrancar) son solo la puerta de entrada.",
        k, BassGrowthModel.VisitasPorPaciente(k), simSettings.PacientesPorDiaMedio);

    if (cr.Enabled)
    {
        var (meseta, mediaFinal) = BassGrowthModel.MediasProyectadas(plan, simSettings, qBocaABoca);
        var ultimo = proyeccion[^1];
        var suelo  = BassGrowthModel.MesetaProyectada(plan, simSettings, 0);

        logger.LogInformation(
            "Crecimiento (difusión de Bass): arranca en {Medio} altas/día y apunta a {Objetivo} visitas/día " +
            "(que es también el AFORO de la consulta) | sin nada de boca a boca la clínica ya llegaría a " +
            "{Suelo:0.0}/día solo con su panel | boca a boca {Origen}: 1 alta más al día por cada {Q:0.0} " +
            "recurrentes satisfechos | la rampa dura lo que la memoria del boca a boca ({Ventana} días) | " +
            "área de {M} personas, techo de seguridad {Max}/día",
            simSettings.PacientesPorDiaMedio, cr.PacientesPorDiaObjetivo, suelo,
            cr.RecurrentesPorPacienteExtra > 0 ? "fijado a mano" : "derivado del objetivo",
            qBocaABoca > 0 ? 1 / qBocaABoca : 0,
            cr.VentanaActividadDias, cr.PoblacionCaptacion, cr.PacientesPorDiaMax);

        logger.LogInformation(
            "Proyección (ESTIMACIÓN determinista, la corrida real la medirá): {Total} visitas " +
            "({Nuevos} altas + {Rec} controles = {PctRec:0} % recurrentes) | se estabiliza en {Meseta:0.0}/día | " +
            "al cierre: {Captados} pacientes captados, {Activos} siguen viniendo = {Pct:0.0} % del área, " +
            "{S} recurrentes satisfechos",
            total, nuevos, recurrentes, total == 0 ? 0 : 100.0 * recurrentes / total, meseta,
            ultimo.CaptadosTotal, ultimo.Activos,
            cr.PoblacionCaptacion == 0 ? 0 : 100.0 * ultimo.Activos / cr.PoblacionCaptacion,
            ultimo.Satisfechos);

        if (suelo >= cr.PacientesPorDiaObjetivo)
            logger.LogWarning(
                "El objetivo ({Objetivo}/día) NO supera lo que el panel produce solo ({Suelo:0.0}/día): la " +
                "clínica no necesita boca a boca para llegar y la curva no crecerá. Sube el objetivo o baja " +
                "Recurrence.VisitasEspontaneasPorPacienteAno.",
                cr.PacientesPorDiaObjetivo, suelo);
        else if (cr.RecurrentesPorPacienteExtra == 0 && meseta < cr.PacientesPorDiaObjetivo * 0.95)
            logger.LogWarning(
                "La clínica NO llega al objetivo de {Objetivo}/día: se queda en {Meseta:0.0}/día. El freno lo " +
                "pone el área de captación ({M} personas) — súbela, o baja el objetivo.",
                cr.PacientesPorDiaObjetivo, meseta, cr.PoblacionCaptacion);

        // La clínica llega a su meseta y luego SE DESINFLA: ha captado a tanta gente del área que ya no le
        // queda a quién captar. Es un síntoma de que el área se le queda pequeña, no del modelo.
        if (mediaFinal < meseta * 0.8)
            logger.LogWarning(
                "La clínica crece hasta {Meseta:0.0}/día y luego DECAE a {Final:0.0}/día: se está quedando sin " +
                "área que captar ({Captados} pacientes distintos de {M} habitantes). Sube " +
                "Crecimiento.PoblacionCaptacion si no es lo que buscas.",
                meseta, mediaFinal, ultimo.CaptadosTotal, cr.PoblacionCaptacion);

        // Las leyes que se pueden juzgar ANTES de sembrar. Vale más una advertencia aquí que descubrir a
        // las 6 horas que la clínica ha registrado a media ciudad.
        ReportarLeyes(
            Invariantes.EvaluarProyeccion(proyeccion, simSettings),
            titulo: "Leyes de la simulación (sobre la proyección — la corrida las medirá de verdad)",
            rotasSonError: false);
    }
    else
    {
        logger.LogInformation(
            "Volumen previsto: {Total} visitas ({Nuevos} altas + {Rec} controles) | media {Media:0.0}/día | " +
            "{Medio} altas/día configuradas | crecimiento DESACTIVADO (las altas salen del plan fijo; los " +
            "controles los sigue poniendo el panel)",
            total, nuevos, recurrentes,
            (double)total / dias.Count, simSettings.PacientesPorDiaMedio);
    }

    if (dias.Count <= 31)
    {
        foreach (var (fecha, t, n, r) in dias)
            logger.LogInformation("   {Fecha:yyyy-MM-dd} {Dia,-9} {Total,3} visitas ({Nuevos} altas, {Rec} controles)",
                fecha, es.DateTimeFormat.GetDayName(fecha.DayOfWeek), t, n, r);
    }
    else
    {
        // El % de recurrentes por mes es la columna que hay que mirar: tiene que SUBIR (el panel madura).
        // Con el cupo fijo salía plana en el 31 %, el primer mes y el último — y nadie lo vio.
        logger.LogInformation("   Desglose por mes ({N} días con atención, demasiados para listarlos):", dias.Count);
        foreach (var mes in dias.GroupBy(d => new { d.Fecha.Year, d.Fecha.Month })
                     .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            var t = mes.Sum(d => d.Total);
            logger.LogInformation(
                "   {Mes,-10} {Anio}  {Dias,2} días  {Total,5} visitas ({Nuevos} altas, {Rec} controles = " +
                "{PctRec,4:0.0} % recurrentes) | media {Media:0.0}/día",
                es.DateTimeFormat.GetMonthName(mes.Key.Month), mes.Key.Year, mes.Count(),
                t, mes.Sum(d => d.Nuevos), mes.Sum(d => d.Recurrentes),
                t == 0 ? 0 : 100.0 * mes.Sum(d => d.Recurrentes) / t,
                (double)t / mes.Count());
        }
    }
}

/// <summary>
/// Imprime el veredicto de las <b>leyes de la simulación</b> (<see cref="Invariantes"/>): las propiedades
/// que una corrida tiene que cumplir para que los datos describan una clínica y no un montón de filas
/// plausibles. Cada ley con su número medido al lado.
///
/// <para>Existe porque el modelo de crecimiento se añadió, rompió la continuidad longitudinal del paciente
/// —el 65 % de los crónicos no volvió jamás a un control, la mitad de la agenda se perdió— y la corrida
/// terminó en "completado" sin una sola advertencia. Doc: <c>leyes_simulacion.md</c>.</para>
/// </summary>
void ReportarLeyes(IReadOnlyList<Ley> leyes, string titulo, bool rotasSonError)
{
    if (leyes.Count == 0) return;

    logger.LogInformation("{Titulo}:", titulo);
    foreach (var ley in leyes)
    {
        var marca = !ley.Aplica ? "—" : ley.Cumple ? "✓" : "✗";
        if (ley.Aplica && !ley.Cumple)
            logger.LogWarning("   {Marca} {Codigo} · {Nombre}: {Medido}", marca, ley.Codigo, ley.Nombre, ley.Medido);
        else if (!ley.Aplica)
            logger.LogInformation("   {Marca} {Codigo} · {Nombre}: no procede en esta corrida (ventana corta o sin datos)",
                marca, ley.Codigo, ley.Nombre);
        else
            logger.LogInformation("   {Marca} {Codigo} · {Nombre}: {Medido}", marca, ley.Codigo, ley.Nombre, ley.Medido);
    }

    foreach (var ley in Invariantes.Rotas(leyes))
    {
        if (rotasSonError) logger.LogError("   ↳ {Codigo}: {Pista}", ley.Codigo, ley.Pista);
        else               logger.LogWarning("   ↳ {Codigo}: {Pista}", ley.Codigo, ley.Pista);
    }
}

async Task<int> EjecutarLimpiezaAsync()
{
    var cleaner = host.Services.GetRequiredService<DataCleaner>();

    var (total, esCotaInferior) = await cleaner.ContarPacientesSimAsync(cts.Token);
    if (total == 0)
    {
        logger.LogInformation("No hay pacientes SIM- que limpiar.");
        return 0;
    }

    // La fricción que antes daba el DELETE explícito ahora es una confirmación interactiva.
    var cantidad = esCotaInferior ? $"al menos {total}" : total.ToString();
    Console.Write($"Se anularán (void) {cantidad} pacientes SIM- y todas sus visitas. ¿Continuar? (s/N): ");
    var respuesta = Console.ReadLine()?.Trim().ToLowerInvariant();
    if (respuesta is not ("s" or "si" or "sí"))
    {
        logger.LogInformation("Limpieza cancelada — no se tocó ningún dato.");
        return 0;
    }

    var (pacientes, visitas, citas) = await cleaner.ClearAsync(cts.Token);
    logger.LogInformation("Limpieza completada: {Pacientes} pacientes y {Visitas} visitas anulados; {Citas} citas canceladas.",
        pacientes, visitas, citas);
    return 0;
}
