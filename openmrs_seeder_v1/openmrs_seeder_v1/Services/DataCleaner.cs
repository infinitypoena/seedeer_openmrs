using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Limpieza de los datos del simulador (antes DELETE /api/seed/clear): pagina los pacientes con
/// identificador SIM-, anula (void) sus visitas y luego al paciente. Rate limiting deliberado
/// (100 ms entre visitas, 200 ms entre pacientes) para no saturar OpenMRS. Los pacientes reales
/// no se tocan (no llevan el prefijo SIM-); los médicos SIM-MED-* son datos de referencia y
/// tampoco se anulan.
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

    /// <summary>Cuenta los pacientes SIM- existentes (para el mensaje de confirmación).</summary>
    public async Task<int> ContarPacientesSimAsync(CancellationToken ct)
    {
        var json = await _client.GetAsync("patient?identifier=SIM-&v=default&limit=1&totalCount=true", ct);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        return doc.TryGetProperty("totalCount", out var total) ? total.GetInt32() : 0;
    }

    public async Task<(int PacientesVoided, int VisitasVoided)> ClearAsync(CancellationToken ct)
    {
        int pacientesVoided = 0;
        int visitasVoided   = 0;

        // Iterar páginas hasta agotar resultados
        int startIndex = 0;
        const int pageSize = 100;

        while (true)
        {
            var json = await _client.GetAsync(
                $"patient?identifier=SIM-&v=full&limit={pageSize}&startIndex={startIndex}", ct);

            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (!doc.TryGetProperty("results", out var results)) break;

            var patients = results.EnumerateArray().ToList();
            if (patients.Count == 0) break;

            foreach (var p in patients)
            {
                if (!p.TryGetProperty("uuid", out var uuidProp)) continue;
                var patientUuid = uuidProp.GetString()!;

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
                    _logger.LogWarning("[Clear] No se pudo borrar paciente {PUuid}: {Msg}",
                        patientUuid, exP.Message);
                }

                await Task.Delay(200, ct);
            }

            // Si devolvió menos de pageSize, ya no hay más páginas
            if (patients.Count < pageSize) break;
            startIndex += pageSize;
        }

        return (pacientesVoided, visitasVoided);
    }
}
