using System.Text;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;

var builder = WebApplication.CreateBuilder(args);

// Settings
var omrsSettings = builder.Configuration.GetSection("OpenMRS").Get<OpenMrsSettings>()!;
var simSettings  = builder.Configuration.GetSection("Simulation").Get<SimulationSettings>()!;

builder.Services.AddSingleton(omrsSettings);
builder.Services.AddSingleton(simSettings);

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
builder.Services.AddTransient<SeedOrchestrator>();

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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "OpenMRS Clinical Simulator", Version = "v1" });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "OpenMRS Clinical Simulator v1");
    c.RoutePrefix = "swagger";
});

var catalogLoader = app.Services.GetRequiredService<CatalogLoader>();
catalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalogs"));

app.MapControllers();
app.Run();
