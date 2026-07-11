using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Seeders;

/// <summary>
/// Citas reales en la agenda de O3 (módulo Bahmni Appointments). Cuando la consulta decide
/// seguimiento (<see cref="SimulatedPatient.FechaSeguimiento"/>), agenda la cita
/// (<c>POST /appointment</c>) con el médico/consultorio de la visita. Al volver el paciente,
/// <see cref="ResolverCitasAsync"/> marca su cita cercana como <c>Completed</c> y las vencidas
/// como <c>Missed</c> (<c>POST /appointments/{uuid}/status-change</c> — ⚠️ plural).
/// <c>Defaults.AppointmentServiceUuid</c> vacío = feature inactiva.
/// </summary>
public class AppointmentSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly int _toleranciaDias;
    private readonly Random _rng;
    private readonly ILogger<AppointmentSeeder> _logger;

    public AppointmentSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        SimulationSettings simSettings,
        ILogger<AppointmentSeeder> logger)
    {
        _client         = client;
        _settings       = settings;
        _toleranciaDias = simSettings.Appointments.ToleranciaDias;
        _rng = new Random(simSettings.RandomSeed + 17);
        _logger         = logger;
    }

    /// <summary>
    /// Seam puro y testeable (sin red): clasifica las citas pendientes respecto a la fecha de la
    /// visita actual. A ±tolerancia → completar (el paciente "vino a su cita"); anterior a la
    /// ventana → perder (no-show); futura → conservar.
    /// </summary>
    public static (List<CitaPendiente> Completar, List<CitaPendiente> Perder, List<CitaPendiente> Conservar)
        ClasificarCitas(IEnumerable<CitaPendiente> citas, DateTime fechaVisita, int toleranciaDias)
    {
        List<CitaPendiente> completar = [], perder = [], conservar = [];
        foreach (var cita in citas)
        {
            var dias = (cita.Fecha.Date - fechaVisita.Date).TotalDays;
            if (Math.Abs(dias) <= toleranciaDias) completar.Add(cita);
            else if (dias < 0)                    perder.Add(cita);
            else                                  conservar.Add(cita);
        }
        return (completar, perder, conservar);
    }

    /// <summary>
    /// Resuelve las citas pendientes del paciente al llegar a una visita: Completed / Missed según
    /// <see cref="ClasificarCitas"/>. Las resueltas se retiran de la lista compartida.
    /// </summary>
    public async Task ResolverCitasAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (patient.CitasPendientes.Count == 0) return;

        var (completar, perder, _) = ClasificarCitas(patient.CitasPendientes, patient.VisitDatetime, _toleranciaDias);

        foreach (var cita in completar)
            if (await CambiarEstadoAsync(patient, cita, "Completed", ct))
                patient.CitasPendientes.Remove(cita);

        foreach (var cita in perder)
            if (await CambiarEstadoAsync(patient, cita, "Missed", ct))
                patient.CitasPendientes.Remove(cita);
    }

    /// <summary>Agenda la cita de seguimiento decidida en la consulta de esta visita.</summary>
    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (patient.FechaSeguimiento is null) return;
        if (string.IsNullOrWhiteSpace(_settings.Defaults.AppointmentServiceUuid)) return;

        // Hora realista: slots de 15 min entre 08:00 y 15:45; duración 15 min.
        var inicio = patient.FechaSeguimiento.Value.Date
            .AddHours(8)
            .AddMinutes(15 * _rng.Next(0, 32));
        var fin = inicio.AddMinutes(15);

        var payload = new Dictionary<string, object?>
        {
            ["patientUuid"]     = patient.OpenMrsUuid,
            ["serviceUuid"]     = _settings.Defaults.AppointmentServiceUuid,
            ["startDateTime"]   = VisitSeeder.FormatDatetime(inicio),
            ["endDateTime"]     = VisitSeeder.FormatDatetime(fin),
            ["appointmentKind"] = "Scheduled",
            ["providers"]       = new[]
            {
                new { uuid = patient.AssignedProviderUuid ?? _settings.Defaults.ProviderUuid, response = "ACCEPTED" }
            },
            ["locationUuid"]    = patient.AssignedLocationUuid ?? _settings.Defaults.LocationUuid,
            ["comments"]        = "SEEDED_BY_SIMULATOR"
        };
        if (!string.IsNullOrWhiteSpace(_settings.Defaults.AppointmentServiceTypeUuid))
            payload["serviceTypeUuid"] = _settings.Defaults.AppointmentServiceTypeUuid;

        try
        {
            var json = await _client.PostAsync("appointment", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("uuid", out var uuidProp) && uuidProp.GetString() is { } uuid)
            {
                patient.CitasPendientes.Add(new CitaPendiente(uuid, inicio));
                _logger.LogInformation("[Appointment] Cita {Fecha:yyyy-MM-dd HH:mm} agendada para {Id}",
                    inicio, patient.Identifier);
            }
            else
            {
                _logger.LogWarning("[Appointment] Respuesta sin uuid al agendar para {Id}", patient.Identifier);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("[Appointment] Error agendando cita para {Id}: {Msg}", patient.Identifier, ex.Message);
        }
    }

    private async Task<bool> CambiarEstadoAsync(SimulatedPatient patient, CitaPendiente cita, string estado, CancellationToken ct)
    {
        var payload = new
        {
            toStatus = estado,
            onDate   = VisitSeeder.FormatDatetime(patient.VisitDatetime)
        };
        try
        {
            // ⚠️ El recurso de transición es PLURAL (/appointments/…), a diferencia del POST de creación.
            await _client.PostAsync($"appointments/{cita.Uuid}/status-change", payload, ct);
            _logger.LogInformation("[Appointment] Cita del {Fecha:yyyy-MM-dd} → {Estado} para {Id}",
                cita.Fecha, estado, patient.Identifier);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Appointment] Error marcando {Estado} la cita {Uuid} de {Id}: {Msg}",
                estado, cita.Uuid, patient.Identifier, ex.Message);
            return false;
        }
    }
}
