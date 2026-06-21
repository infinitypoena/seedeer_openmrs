using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Controllers;

[ApiController]
[Route("api/seed")]
public class SeedController : ControllerBase
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _omrsSettings;
    private readonly SimulationSettings _simSettings;
    private readonly SeedProgressTracker _tracker;
    private readonly CatalogLoader _catalogs;
    private readonly SeedOrchestrator _orchestrator;
    private readonly ILogger<SeedController> _logger;

    public SeedController(
        OpenMrsRestClient client,
        OpenMrsSettings omrsSettings,
        SimulationSettings simSettings,
        SeedProgressTracker tracker,
        CatalogLoader catalogs,
        SeedOrchestrator orchestrator,
        ILogger<SeedController> logger)
    {
        _client       = client;
        _omrsSettings = omrsSettings;
        _simSettings  = simSettings;
        _tracker      = tracker;
        _catalogs     = catalogs;
        _orchestrator = orchestrator;
        _logger       = logger;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var openmrsOnline = await _client.PingAsync(ct);

        return Ok(new
        {
            openmrs = new
            {
                online   = openmrsOnline,
                baseUrl  = _omrsSettings.RestApi.BaseUrl,
                seedMode = _omrsSettings.SeedMode
            },
            simulation = new
            {
                clinicType            = _simSettings.ClinicType,
                startDate             = _simSettings.StartDate.ToString("yyyy-MM-dd"),
                endDate               = _simSettings.EndDate.ToString("yyyy-MM-dd"),
                pacientesPorDiaMedio  = _simSettings.PacientesPorDiaMedio,
                porcentajeRecurrentes = _simSettings.PorcentajeRecurrentes,
                locale                = _simSettings.Locale,
                randomSeed            = _simSettings.RandomSeed
            },
            referralProbabilities = new
            {
                labOrder     = _simSettings.ReferralProbabilities.LabOrder,
                clinicalExam = _simSettings.ReferralProbabilities.ClinicalExam,
                drugOrder    = _simSettings.ReferralProbabilities.DrugOrder
            },
            allergy = new
            {
                baseProbabilityMin       = _simSettings.Allergy.BaseProbabilityMin,
                baseProbabilityMax       = _simSettings.Allergy.BaseProbabilityMax,
                secondAllergyProbability = _simSettings.Allergy.SecondAllergyProbability,
                thirdAllergyProbability  = _simSettings.Allergy.ThirdAllergyProbability,
                maxAllergies             = _simSettings.Allergy.MaxAllergies
            },
            catalogs = new
            {
                epidemiologyProfile = _catalogs.EpidemiologyProfile.Count,
                diagnosticos        = _catalogs.Diagnosticos.Count,
                medicamentos        = _catalogs.Medicamentos.Count,
                laboratorios        = _catalogs.Laboratorios.Count,
                examenesClinicos    = _catalogs.ExamenesClinicos.Count,
                alergenos           = _catalogs.Alergenos.Count,
                motivosConsulta     = _catalogs.MotivosConsulta.Count
            }
        });
    }

    [HttpPost("run")]
    public IActionResult Run([FromBody] RunRequest? request)
    {
        var runId = _tracker.CreateRun();
        _tracker.Update(runId, r => { r.Etapa = "iniciando"; r.Porcentaje = 0; });

        var orchestrator = _orchestrator;
        var tracker      = _tracker;

        _ = Task.Run(async () =>
        {
            try
            {
                await orchestrator.RunAsync(runId, tracker, CancellationToken.None);
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
        });

        return Accepted(new { runId });
    }

    [HttpGet("progress/{runId:guid}")]
    public IActionResult Progress(Guid runId)
    {
        var run = _tracker.GetRun(runId);
        if (run is null) return NotFound(new { error = $"Run {runId} no encontrado" });

        return Ok(new
        {
            runId            = run.RunId,
            porcentaje       = run.Porcentaje,
            etapa            = run.Etapa,
            pacientesCreados = run.PacientesCreados,
            diasProcesados   = run.DiasProcesados,
            totalDias        = run.TotalDias,
            fechaActual      = run.FechaActual,
            completado       = run.Completado,
            errores          = run.Errores,
            inicio           = run.Inicio
        });
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        int pacientesVoided = 0;
        int visitasVoided   = 0;

        try
        {
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
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, pacientesVoided, visitasVoided });
        }

        return Ok(new { pacientesVoided, visitasVoided });
    }
}

public record RunRequest(string? Step);
