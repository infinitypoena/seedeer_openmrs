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
// Exit codes: 0 = corrida completada · 1 = fallo del proceso · 2 = OpenMRS inaccesible / uso inválido /
// catálogos inválidos (en los tres casos no se toca ningún dato).

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

// Servicios singleton (stateless, seguros para reusar)
builder.Services.AddSingleton<SeedProgressTracker>();
builder.Services.AddSingleton<RunStats>();
builder.Services.AddSingleton<CatalogLoader>();
builder.Services.AddSingleton<DailyScheduleGenerator>();
builder.Services.AddSingleton<PatientProfileGenerator>();
builder.Services.AddSingleton<EpidemiologySelector>();
builder.Services.AddSingleton<ClimateResolver>();

// Asignador de consultorio/médico (transient: hace REST por corrida, como los seeders)
builder.Services.AddTransient<ClinicResourceAssigner>();

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

// Claves del JSON que el binding ignoró en silencio (típicamente parámetros renombrados/obsoletos).
var clavesDesconocidas = SettingsValidator
    .FindUnknownKeys(builder.Configuration.GetSection("Simulation"), typeof(SimulationSettings))
    .Concat(SettingsValidator.FindUnknownKeys(builder.Configuration.GetSection("OpenMRS"), typeof(OpenMrsSettings)));
foreach (var clave in clavesDesconocidas)
    logger.LogWarning("Clave de configuración desconocida (ignorada por el binding): {Clave}", clave);

// ══ Etapa 1/3 · Catálogos ═══════════════════════════════════════════════════════════════════════
// Fail-fast: el loader es mudo (CSV ausente → lista vacía, booleano mal escrito → false), así que una
// errata se manifestaría como una feature apagada en silencio o como comportamiento raro a mitad de la
// corrida. Se carga, se informa de lo cargado y se valida ANTES de tocar OpenMRS.
var catalogLoader = host.Services.GetRequiredService<CatalogLoader>();
catalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalogs"));

logger.LogInformation("══ Etapa 1/4 · Validación de catálogos ══");
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
    default:
        logger.LogError("Argumento no reconocido: '{Modo}'. Uso: dotnet run [-- run|clear]", modo);
        return 2;
}

async Task<int> EjecutarSimulacionAsync()
{
    var tracker      = host.Services.GetRequiredService<SeedProgressTracker>();
    var orchestrator = host.Services.GetRequiredService<SeedOrchestrator>();

    // ══ Etapa 2/4 · Días a simular ═══════════════════════════════════════════════════════════════
    // El plan se genera aquí una sola vez y el orquestador reusa ESTE mismo (PlanificarDias lo cachea):
    // el informe describe la corrida que realmente se va a ejecutar, no una tirada distinta.
    var plan = orchestrator.PlanificarDias();
    ReportarPlan(plan);

    // ══ Etapa 3/4 · Ejecución ════════════════════════════════════════════════════════════════════
    // Margen para abortar (Ctrl+C) tras leer el informe: a partir de aquí se escribe en OpenMRS.
    logger.LogInformation("══ Etapa 3/4 · Ejecución — comenzando en {S} s (Ctrl+C para abortar) ══",
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

    // ══ Etapa 4/4 · Resumen final ════════════════════════════════════════════════════════════════
    var run   = tracker.GetRun(runId)!;
    var stats = host.Services.GetRequiredService<RunStats>();

    logger.LogInformation("══ Etapa 4/4 · Resumen final ══");
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

    EsperarTecla();
    return run.Etapa == "error" ? 1 : 0;
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
/// consola. Los volúmenes ya vienen sorteados (peso del día de la semana + normal), así que esto es
/// exactamente lo que se va a sembrar, no una estimación.
/// </summary>
void ReportarPlan(IReadOnlyList<DailySchedule> plan)
{
    var es          = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
    var conAtencion = plan.Where(d => d.TotalPatients > 0).ToList();
    var total       = plan.Sum(d => d.TotalPatients);
    var nuevos      = plan.Sum(d => d.NuevosPacientes);
    var recurrentes = plan.Sum(d => d.PacientesRecurrentes);

    logger.LogInformation("══ Etapa 2/4 · Días a simular ══");
    logger.LogInformation(
        "Ventana: {Inicio:yyyy-MM-dd} → {Fin:yyyy-MM-dd} | {Dias} días naturales, {ConAtencion} con " +
        "atención ({Cerrados} cerrados por peso 0 en WeekdayWeights)",
        simSettings.StartDate, simSettings.EndDate,
        plan.Count, conAtencion.Count, plan.Count - conAtencion.Count);
    logger.LogInformation(
        "Volumen previsto: {Total} visitas ({Nuevos} de pacientes nuevos + {Rec} de recurrentes) | " +
        "media {Media:0.0}/día de atención | {Medio} pac/día medio configurado, {Pct}% recurrentes",
        total, nuevos, recurrentes,
        conAtencion.Count == 0 ? 0 : (double)total / conAtencion.Count,
        simSettings.PacientesPorDiaMedio, simSettings.PorcentajeRecurrentes);

    if (conAtencion.Count == 0)
    {
        logger.LogWarning("Ningún día de la ventana tiene pacientes — revisa WeekdayWeights y las fechas.");
        return;
    }

    if (conAtencion.Count <= 31)
    {
        foreach (var d in conAtencion)
            logger.LogInformation("   {Fecha:yyyy-MM-dd} {Dia,-9} {Total,3} visitas ({Nuevos} nuevos, {Rec} recurrentes)",
                d.Date, es.DateTimeFormat.GetDayName(d.Date.DayOfWeek),
                d.TotalPatients, d.NuevosPacientes, d.PacientesRecurrentes);
    }
    else
    {
        logger.LogInformation("   Desglose por mes ({N} días con atención, demasiados para listarlos):", conAtencion.Count);
        foreach (var mes in conAtencion.GroupBy(d => new { d.Date.Year, d.Date.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
            logger.LogInformation("   {Mes,-10} {Anio}  {Dias,2} días  {Total,5} visitas ({Nuevos} nuevos, {Rec} recurrentes)",
                es.DateTimeFormat.GetMonthName(mes.Key.Month), mes.Key.Year, mes.Count(),
                mes.Sum(d => d.TotalPatients), mes.Sum(d => d.NuevosPacientes), mes.Sum(d => d.PacientesRecurrentes));
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
