using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

public class LabOrderSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly double _labOrderProb;
    private readonly double _urgentProb;
    private readonly double _labResultProb;
    private readonly int _labVigenciaDias;
    private readonly Random _rng;
    private readonly ILogger<LabOrderSeeder> _logger;

    public LabOrderSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        SimulationSettings simSettings,
        ILogger<LabOrderSeeder> logger)
    {
        _client        = client;
        _settings      = settings;
        _catalogs      = catalogs;
        _labOrderProb  = simSettings.ReferralProbabilities.LabOrder;
        _urgentProb    = simSettings.ReferralProbabilities.Urgent;
        _labResultProb = simSettings.ReferralProbabilities.LabResult;
        _labVigenciaDias = simSettings.Orders.LabVigenciaDias;
        _rng = new Random(simSettings.RandomSeed + 14);
        _logger        = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patient.ConsultaEncounterUuid))
        {
            _logger.LogWarning("[LabOrder] Skip: sin encounter de consulta para {Id}", patient.Identifier);
            return;
        }

        var debeOrden = patient.TodosDiagnosticos.Any(d => d.RequiereLab)
            ? _rng.NextDouble() < 0.80
            : _rng.NextDouble() < _labOrderProb;

        if (!debeOrden) return;

        var fechaVisita = DateOnly.FromDateTime(patient.VisitDatetime);
        var candidatos = _catalogs.Laboratorios
            .Where(l => patient.Categorias.Any(c => AplicaCategoria(l, c)))
            .Where(l => !OrderVigencia.EstaActivo(patient.OrderedConcepts, l.CielUuid, fechaVisita)) // solo si no hay una orden aún vigente
            .ToList();

        if (candidatos.Count == 0) return;

        var cantidad = _rng.NextDouble() < 0.40 ? 2 : 1;
        var elegidos = candidatos.OrderBy(_ => _rng.Next()).Take(cantidad).ToList();

        // Sets para el generador de resultados (categorías + diagnósticos del paciente)
        var categorias = patient.Categorias as ISet<string> ?? new HashSet<string>(patient.Categorias);
        var dxUuids    = patient.TodosDiagnosticos.Select(d => d.CielUuid).ToHashSet();

        int ordenesOk = 0, resultadosOk = 0, diferidos = 0;
        foreach (var lab in elegidos)
        {
            var esUrgente = patient.TodosDiagnosticos.Any(d => d.Severidad == "grave")
                ? _rng.NextDouble() < 0.50
                : _rng.NextDouble() < _urgentProb;

            var orderUuid = await PostOrderAsync(patient, lab.CielUuid, esUrgente ? "STAT" : "ROUTINE", ct);
            if (orderUuid is null) continue;

            ordenesOk++;
            // La orden queda activa hasta su autoExpireDate (fecha de la visita + vigencia): hasta
            // entonces no se re-ordena el mismo test; después, un control crónico vuelve a pedirlo.
            patient.OrderedConcepts[lab.CielUuid] = fechaVisita.AddDays(_labVigenciaDias);

            // Generar el resultado con el contexto clínico de ESTA visita (aunque se registre después)
            var result = LabResultGenerator.Generar(lab, categorias, dxUuids, _rng);
            var componentes = lab.Datatype == "panel"
                ? LabResultGenerator.GenerarComponentes(
                    _catalogs.Paneles.Where(p => p.PanelUuid == lab.CielUuid), categorias, _rng)
                : null;
            var sinResultado = result.Tipo == LabResultGenerator.TipoResultado.Ninguno &&
                               (componentes is null || componentes.Count == 0);
            if (sinResultado) continue; // imagen / panel sin componentes catalogados

            if (_rng.NextDouble() < _labResultProb)
            {
                // El resultado "vuelve" el mismo día
                var ok = componentes is { Count: > 0 }
                    ? await PostPanelObsAsync(patient, lab.CielUuid, orderUuid, componentes, patient.VisitDatetime, ct)
                    : await PostResultObsAsync(patient, lab.CielUuid, orderUuid, result, ct);
                if (ok) resultadosOk++;
            }
            else
            {
                // Retraso realista: la orden queda pendiente y el valor llega en la próxima visita
                patient.ResultadosPendientes.Add(new ResultadoPendiente(
                    orderUuid, lab.CielUuid, result.Numerico, result.CodedUuid, componentes));
                diferidos++;
            }
        }

        _logger.LogInformation("[LabOrder] {N}/{Total} órdenes + {R} resultados ({D} diferidos) para {Id}",
            ordenesOk, elegidos.Count, resultadosOk, diferidos, patient.Identifier);
    }

    /// <summary>
    /// Registra los resultados que quedaron pendientes en visitas anteriores ("ya llegó el resultado"),
    /// con la fecha de la visita actual. Llamado al inicio de cada visita del paciente.
    /// </summary>
    public async Task ProcesarPendientesAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (patient.ResultadosPendientes.Count == 0) return;

        var entregados = 0;
        foreach (var p in patient.ResultadosPendientes.ToList())
        {
            var ok = p.Componentes is { Count: > 0 }
                ? await PostPanelObsAsync(patient, p.ConceptUuid, p.OrderUuid, p.Componentes, patient.VisitDatetime, ct)
                : await PostResultObsAsync(patient, p.ConceptUuid, p.OrderUuid,
                    p.Numerico is not null
                        ? new LabResultGenerator.LabResult(LabResultGenerator.TipoResultado.Numerico, p.Numerico, null)
                        : new LabResultGenerator.LabResult(LabResultGenerator.TipoResultado.Codificado, null, p.CodedUuid),
                    ct);
            if (ok)
            {
                patient.ResultadosPendientes.Remove(p);
                entregados++;
            }
        }
        if (entregados > 0)
            _logger.LogInformation("[LabOrder] {N} resultado(s) pendiente(s) entregados para {Id}",
                entregados, patient.Identifier);
    }

    /// <summary>Registra el resultado de un panel como obs-group ligado a la orden (padre + un hijo por componente).</summary>
    private async Task<bool> PostPanelObsAsync(
        SimulatedPatient patient, string panelUuid, string orderUuid,
        List<(string ConceptUuid, double Valor)> componentes, DateTime fecha, CancellationToken ct)
    {
        var obsDatetime = VisitSeeder.FormatDatetime(fecha);
        var payload = new
        {
            concept     = panelUuid,
            person      = patient.OpenMrsUuid,
            encounter   = patient.ConsultaEncounterUuid,
            order       = orderUuid,
            obsDatetime,
            groupMembers = componentes.Select(c => new
            {
                concept     = c.ConceptUuid,
                person      = patient.OpenMrsUuid,
                obsDatetime,
                value       = c.Valor
            }).ToArray()
        };

        try
        {
            await _client.PostAsync("obs", payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LabOrder] Error en panel {Concept} para {Id}: {Msg}",
                panelUuid, patient.Identifier, ex.Message);
            return false;
        }
    }

    /// <summary>Crea la orden y devuelve su UUID (o null si falla).</summary>
    private async Task<string?> PostOrderAsync(SimulatedPatient patient, string conceptUuid, string urgency, CancellationToken ct)
    {
        var payload = new
        {
            type        = "testorder",
            patient     = patient.OpenMrsUuid,
            concept     = conceptUuid,
            encounter   = patient.ConsultaEncounterUuid,
            orderer     = patient.AssignedProviderUuid ?? _settings.Defaults.ProviderUuid,
            careSetting = _settings.Defaults.OutpatientCareSettingUuid,
            urgency,
            // Sin esto OpenMRS usa el reloj real: la orden quedaba fechada el día de la corrida,
            // no el de la visita simulada. Debe coincidir con el datetime del encounter de consulta
            // (no puede ser anterior a él).
            dateActivated = VisitSeeder.FormatDatetime(ConsultaSeeder.FechaConsulta(patient)),
            // Caducidad: pasada la vigencia la orden deja de estar activa, así un control crónico
            // posterior puede volver a pedir el mismo test sin AmbiguousOrderException.
            autoExpireDate = VisitSeeder.FormatDatetime(
                ConsultaSeeder.FechaConsulta(patient).AddDays(_labVigenciaDias))
        };

        try
        {
            var json = await _client.PostAsync("order", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            return doc.TryGetProperty("uuid", out var uuid) ? uuid.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LabOrder] Error en orden para {Id}: {Msg}", patient.Identifier, ex.Message);
            return null;
        }
    }

    /// <summary>Registra el resultado como obs ligada a la orden (numérico → número; codificado → UUID de respuesta).</summary>
    private async Task<bool> PostResultObsAsync(
        SimulatedPatient patient, string conceptUuid, string orderUuid,
        LabResultGenerator.LabResult result, CancellationToken ct)
    {
        object value = result.Tipo == LabResultGenerator.TipoResultado.Numerico
            ? result.Numerico!.Value
            : result.CodedUuid!;

        var payload = new
        {
            concept     = conceptUuid,
            person      = patient.OpenMrsUuid,
            encounter   = patient.ConsultaEncounterUuid,
            order       = orderUuid,
            obsDatetime = VisitSeeder.FormatDatetime(patient.VisitDatetime),
            value
        };

        try
        {
            await _client.PostAsync("obs", payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LabOrder] Error en resultado {Concept} para {Id}: {Msg}",
                conceptUuid, patient.Identifier, ex.Message);
            return false;
        }
    }

    private static bool AplicaCategoria(Models.Catalogs.LaboratorioEntry l, string cat) => cat switch
    {
        "respiratorio"   => l.AplicaRespiratorio,
        "cardiovascular" => l.AplicaCardiovascular,
        "diabetes"       => l.AplicaDiabetes,
        "digestivo"      => l.AplicaDigestivo,
        "osteomuscular"  => l.AplicaOsteomuscular,
        "urologico"      => l.AplicaUrologico,
        "infeccioso"     => l.AplicaInfeccioso,
        "endocrino"      => l.AplicaEndocrino,
        "neurologico"     => l.AplicaNeurologico,
        "dermatologico"   => l.AplicaDermatologico,
        "salud_mental"    => l.AplicaSaludMental,
        "ginecoobstetrico"=> l.AplicaGinecoobstetrico,
        "trauma"          => l.AplicaTrauma,
        _ => false
    };
}
