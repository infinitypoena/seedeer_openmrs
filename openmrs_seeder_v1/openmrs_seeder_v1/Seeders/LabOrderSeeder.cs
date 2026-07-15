using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

/// <summary>
/// Decide QUÉ exámenes pide el médico y crea la orden. El ciclo posterior (toma de la muestra, resultado,
/// validación o rechazo) lo lleva <see cref="LabWorkflowSeeder"/>: aquí solo se firma la petición.
/// </summary>
public class LabOrderSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly CatalogLoader _catalogs;
    private readonly LabWorkflowSeeder _workflow;
    private readonly double _labOrderProb;
    private readonly double _urgentProb;
    private readonly int _labVigenciaDias;
    private readonly Random _rng;
    private readonly ILogger<LabOrderSeeder> _logger;

    public LabOrderSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        CatalogLoader catalogs,
        LabWorkflowSeeder workflow,
        SimulationSettings simSettings,
        ILogger<LabOrderSeeder> logger)
    {
        _client        = client;
        _settings      = settings;
        _catalogs      = catalogs;
        _workflow      = workflow;
        _labOrderProb  = simSettings.ReferralProbabilities.LabOrder;
        _urgentProb    = simSettings.ReferralProbabilities.Urgent;
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

        int ordenesOk = 0;
        foreach (var lab in elegidos)
        {
            // Al paciente que se va al hospital, los labs son de ESTABILIZACIÓN: siempre urgentes, no la
            // mitad de las veces. Un cuadro grave que se queda en la clínica, la mitad; el resto, la
            // probabilidad base.
            var esUrgente = patient.Referido
                || (patient.TodosDiagnosticos.Any(d => d.Severidad == "grave")
                    ? _rng.NextDouble() < 0.50
                    : _rng.NextDouble() < _urgentProb);

            var orderUuid = await PostOrderAsync(patient, lab, esUrgente ? "STAT" : "ROUTINE", fechaVisita, ct);
            if (orderUuid is null) continue;

            ordenesOk++;
            // La orden queda activa hasta su autoExpireDate (fecha de la visita + vigencia): hasta
            // entonces no se re-ordena el mismo test; después, un control crónico vuelve a pedirlo.
            patient.OrderedConcepts[lab.CielUuid] = fechaVisita.AddDays(_labVigenciaDias);

            // El resultado se genera con el contexto clínico de ESTA visita, aunque el laboratorio
            // externo lo entregue días después.
            var result = LabResultGenerator.Generar(lab, categorias, dxUuids, _rng);
            var componentes = lab.Datatype == "panel"
                ? LabResultGenerator.GenerarComponentes(
                    _catalogs.Paneles.Where(p => p.PanelUuid == lab.CielUuid), categorias, _rng)
                : null;

            await _workflow.ProcesarOrdenAsync(patient, lab, orderUuid, result, componentes, ct);
        }

        _logger.LogInformation("[LabOrder] {N}/{Total} órdenes para {Id}",
            ordenesOk, elegidos.Count, patient.Identifier);
    }

    /// <summary>Crea la orden y devuelve su UUID (o null si falla).</summary>
    private async Task<string?> PostOrderAsync(
        SimulatedPatient patient, Models.Catalogs.LaboratorioEntry lab, string urgency,
        DateOnly fechaVisita, CancellationToken ct)
    {
        var payload = new
        {
            type        = "testorder",
            patient     = patient.OpenMrsUuid,
            concept     = lab.CielUuid,
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
                ConsultaSeeder.FechaConsulta(patient).AddDays(_labVigenciaDias)),
            // Nº de muestra e instrucción al laboratorio: el que sale del catálogo decide si el examen
            // se procesa aquí o se refiere a un laboratorio externo.
            accessionNumber    = _workflow.SiguienteNumeroMuestra(fechaVisita),
            commentToFulfiller = LabWorkflow.ComentarioAlLaboratorio(lab)
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
