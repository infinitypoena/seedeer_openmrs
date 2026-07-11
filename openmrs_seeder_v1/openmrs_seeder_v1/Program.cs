using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;

// Simulador clínico OpenMRS — ejecución batch: `dotnet run` corre la simulación completa según
// appsettings.json y termina. `dotnet run -- clear` anula (void) todos los datos SIM- previos.
// Exit codes: 0 = corrida completada · 1 = fallo del proceso · 2 = OpenMRS inaccesible / uso inválido.

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

var catalogLoader = host.Services.GetRequiredService<CatalogLoader>();
catalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalogs"));

// Ctrl+C → cancelación limpia (los datos ya insertados persisten)
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.LogWarning("Cancelación solicitada (Ctrl+C) — cerrando de forma limpia…");
    cts.Cancel();
};

// Resumen inicial (el viejo GET /api/seed/status, ahora por consola)
var client = host.Services.GetRequiredService<OpenMrsRestClient>();
var online = await client.PingAsync(cts.Token);
logger.LogInformation(
    "OpenMRS: {Estado} ({BaseUrl}) | Ventana: {Start:yyyy-MM-dd} → {End:yyyy-MM-dd} | " +
    "{Volumen} pac/día medio, {Recurrentes}% recurrentes, seed {Seed}",
    online ? "ONLINE" : "OFFLINE", omrsSettings.RestApi.BaseUrl,
    simSettings.StartDate, simSettings.EndDate,
    simSettings.PacientesPorDiaMedio, simSettings.PorcentajeRecurrentes, simSettings.RandomSeed);
logger.LogInformation(
    "Catálogos: {Dx} diagnósticos, {Med} medicamentos, {Lab} laboratorios, {Aler} alérgenos, " +
    "{Cons} consultorios, {Prog} programas",
    catalogLoader.Diagnosticos.Count, catalogLoader.Medicamentos.Count,
    catalogLoader.Laboratorios.Count, catalogLoader.Alergenos.Count,
    catalogLoader.Consultorios.Count, catalogLoader.Programas.Count);

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

    var run = tracker.GetRun(runId)!;
    logger.LogInformation(
        "Resumen final: etapa '{Etapa}' | {Pacientes} pacientes creados | {Dias}/{Total} días | " +
        "{Errores} errores de proceso | {Operacion} errores de operación",
        run.Etapa, run.PacientesCreados, run.DiasProcesados, run.TotalDias,
        run.Errores.Count, errorTally.Total);
    foreach (var error in run.Errores)
        logger.LogWarning("  [proceso] {Error}", error);
    if (errorTally.Total > 0)
    {
        logger.LogWarning("Errores de operación por componente: {Desglose}", errorTally.Desglose());
        foreach (var mensaje in errorTally.Mensajes)
            logger.LogWarning("  {Mensaje}", mensaje);
    }

    return run.Etapa == "error" ? 1 : 0;
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
