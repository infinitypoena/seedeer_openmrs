using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Limpieza de los datos del simulador (antes DELETE /api/seed/clear): pagina los pacientes con
/// identificador SIM-, anula (void) sus visitas y luego al paciente. ⚠️ Los pacientes anulados
/// desaparecen del resultado de la búsqueda, así que la lista encoge mientras se itera: cada
/// página se pide desde el frente (startIndex = pacientes fallidos, no un avance acumulado) —
/// avanzar a ciegas saltaba pacientes y acababa en un 500 fromIndex &gt; toIndex de OpenMRS.
/// Rate limiting deliberado (100 ms entre visitas, 200 ms entre pacientes) para no saturar
/// OpenMRS. Los pacientes reales no se tocan (no llevan el prefijo SIM-); los médicos SIM-MED-*
/// son datos de referencia y tampoco se anulan.
/// </summary>
public class DataCleaner
{
    private readonly OpenMrsRestClient _client;
    private readonly ILogger<DataCleaner> _logger;

    public DataCleaner(OpenMrsRestClient client, ILogger<DataCleaner> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Cuenta los pacientes SIM- existentes para el mensaje de confirmación.
    /// ⚠️ OpenMRS capea totalCount en 1000: devuelve (conteo, esCotaInferior) para que el prompt
    /// diga "al menos 1000" y no subestime la operación que se está confirmando.
    /// </summary>
    public async Task<(int Total, bool EsCotaInferior)> ContarPacientesSimAsync(CancellationToken ct)
    {
        var json = await _client.GetAsync("patient?identifier=SIM-&v=default&limit=1&totalCount=true", ct);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var total = doc.TryGetProperty("totalCount", out var t) ? t.GetInt32() : 0;
        return (total, total >= 1000);
    }

    public async Task<(int PacientesVoided, int VisitasVoided, int CitasCanceladas)> ClearAsync(CancellationToken ct)
    {
        int pacientesVoided = 0;
        int visitasVoided   = 0;
        int citasCanceladas = 0;

        // Iterar páginas hasta agotar resultados. Como el void saca al paciente de la búsqueda,
        // siempre se lee el frente de la lista viva; los fallidos (que sí permanecen) se dejan
        // atrás con el startIndex para no reintentarlos en bucle infinito.
        int fallidos = 0;
        const int pageSize = 100;

        while (true)
        {
            var json = await _client.GetAsync(
                $"patient?identifier=SIM-&v=full&limit={pageSize}&startIndex={fallidos}", ct);

            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (!doc.TryGetProperty("results", out var results)) break;

            var patients = results.EnumerateArray().ToList();
            if (patients.Count == 0) break;

            foreach (var p in patients)
            {
                if (!p.TryGetProperty("uuid", out var uuidProp)) continue;
                var patientUuid = uuidProp.GetString()!;

                // Cancelar citas Scheduled/CheckedIn del paciente (una vez anulado quedan huérfanas en la agenda)
                citasCanceladas += await CancelarCitasAsync(patientUuid, ct);

                // Void visitas del paciente
                try
                {
                    var visitsJson = await _client.GetAsync(
                        $"visit?patient={patientUuid}&v=full&limit=100", ct);
                    var visitsDoc = JsonSerializer.Deserialize<JsonElement>(visitsJson);
                    if (visitsDoc.TryGetProperty("results", out var visits))
                    {
                        foreach (var v in visits.EnumerateArray())
                        {
                            if (!v.TryGetProperty("uuid", out var vUuid)) continue;
                            try
                            {
                                await _client.DeleteAsync(
                                    $"visit/{vUuid.GetString()}?reason=SEEDED_BY_SIMULATOR", ct);
                                visitasVoided++;
                            }
                            catch (Exception exV)
                            {
                                _logger.LogWarning("[Clear] No se pudo borrar visita {VUuid}: {Msg}",
                                    vUuid.GetString(), exV.Message);
                            }
                            await Task.Delay(100, ct);
                        }
                    }
                }
                catch (Exception exVL)
                {
                    _logger.LogWarning("[Clear] Error obteniendo visitas de paciente {PUuid}: {Msg}",
                        patientUuid, exVL.Message);
                }

                try
                {
                    await _client.DeleteAsync(
                        $"patient/{patientUuid}?reason=SEEDED_BY_SIMULATOR", ct);
                    pacientesVoided++;
                }
                catch (Exception exP)
                {
                    fallidos++;
                    _logger.LogWarning("[Clear] No se pudo borrar paciente {PUuid}: {Msg}",
                        patientUuid, exP.Message);
                }

                await Task.Delay(200, ct);
            }
        }

        return (pacientesVoided, visitasVoided, citasCanceladas);
    }

    /// <summary>
    /// Cancela las citas activas (Scheduled/CheckedIn) del paciente vía el módulo Bahmni Appointments.
    /// Completed/Missed no se tocan (las transiciones desde estados terminales son rechazadas).
    /// </summary>
    private async Task<int> CancelarCitasAsync(string patientUuid, CancellationToken ct)
    {
        int canceladas = 0;
        try
        {
            var json = await _client.PostAsync("appointments/search",
                new { patientUuid, startDate = "2000-01-01T00:00:00.000+0000" }, ct);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.ValueKind != JsonValueKind.Array) return 0;

            foreach (var cita in doc.EnumerateArray())
            {
                if (!cita.TryGetProperty("status", out var st) ||
                    st.GetString() is not ("Scheduled" or "CheckedIn")) continue;
                if (!cita.TryGetProperty("uuid", out var u)) continue;
                try
                {
                    await _client.PostAsync($"appointments/{u.GetString()}/status-change",
                        new { toStatus = "Cancelled", onDate = Seeders.VisitSeeder.FormatDatetime(DateTime.Now) }, ct);
                    canceladas++;
                }
                catch (Exception exC)
                {
                    _logger.LogWarning("[Clear] No se pudo cancelar cita {Uuid}: {Msg}", u.GetString(), exC.Message);
                }
                await Task.Delay(100, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Clear] Error buscando citas de {PUuid}: {Msg}", patientUuid, ex.Message);
        }
        return canceladas;
    }
}
