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
    private readonly ChequeoSettings _chequeo;
    private readonly Random _rng;
    // Tiradas del examen a petición del paciente. RNG propio (+24): con Chequeo.Enabled=false no se
    // tira nunca y el flujo del seeder (+14) queda intacto.
    private readonly Random _rngChequeo;
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
        _chequeo       = simSettings.Chequeo;
        _rng = new Random(simSettings.RandomSeed + 14);
        _rngChequeo = new Random(simSettings.RandomSeed + 24);
        _logger        = logger;
    }

    public async Task SeedAsync(SimulatedPatient patient, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patient.ConsultaEncounterUuid))
        {
            _logger.LogWarning(Eventos.ItemPerdido, "[LabOrder] Skip: sin encounter de consulta para {Id}", patient.Identifier);
            return;
        }

        var fechaVisita = DateOnly.FromDateTime(patient.VisitDatetime);

        List<Models.Catalogs.LaboratorioEntry> elegidos;
        Models.Catalogs.LaboratorioEntry? pedidoPorPaciente = null;

        if (patient.EsChequeo)
        {
            // Visita de chequeo voluntario: el paciente vino expresamente a hacerse exámenes comunes.
            elegidos = LabOrderSelector.SeleccionarChequeo(
                _catalogs.Laboratorios, patient.OrderedConcepts, fechaVisita,
                _chequeo.MinLabs, _chequeo.MaxLabs, _rng);
        }
        else
        {
            // Selección en el seam puro. Los confirmatorios se fuerzan sobre los dxs EVALUABLES (en un
            // control post-alta, el episodio que el hospital ya resolvió no re-ordena su examen — ley L9).
            var dxConfirmables = ConsultaSeeder.DxsEvaluables(patient).Select(d => d.CielUuid).ToList();
            elegidos = LabOrderSelector.Seleccionar(
                _catalogs.Laboratorios, _catalogs.LabsConfirmatorios,
                patient.Categorias, dxConfirmables,
                requiereLab: patient.TodosDiagnosticos.Any(d => d.RequiereLab),
                probBase: _labOrderProb,
                patient.OrderedConcepts, fechaVisita, _rng);

            // El enfermo que "aprovecha" la consulta y pide además un examen común por su cuenta.
            if (_chequeo.Enabled && _rngChequeo.NextDouble() < _chequeo.ProbExamenAdicional)
            {
                pedidoPorPaciente = LabOrderSelector.ExamenAPeticion(
                    _catalogs.Laboratorios, elegidos.Select(l => l.CielUuid).ToList(),
                    patient.OrderedConcepts, fechaVisita, _rngChequeo);
                if (pedidoPorPaciente is not null) elegidos.Add(pedidoPorPaciente);
            }
        }

        if (elegidos.Count == 0) return;

        // Sets para el generador de resultados (categorías + diagnósticos del paciente)
        var categorias = patient.Categorias as ISet<string> ?? new HashSet<string>(patient.Categorias);
        var dxUuids    = patient.TodosDiagnosticos.Select(d => d.CielUuid).ToHashSet();

        int ordenesOk = 0;
        foreach (var lab in elegidos)
        {
            // El examen que pidió el propio paciente (chequeo o "ya que estoy") nunca es urgente.
            var solicitadoPorPaciente = patient.EsChequeo || ReferenceEquals(lab, pedidoPorPaciente);

            // Al paciente que se va al hospital, los labs son de ESTABILIZACIÓN: siempre urgentes, no la
            // mitad de las veces. Un cuadro grave que se queda en la clínica, la mitad; el resto, la
            // probabilidad base.
            var esUrgente = !solicitadoPorPaciente &&
                (patient.Referido
                 || (patient.TodosDiagnosticos.Any(d => d.Severidad == "grave")
                     ? _rng.NextDouble() < 0.50
                     : _rng.NextDouble() < _urgentProb));

            var orderUuid = await PostOrderAsync(
                patient, lab, esUrgente ? "STAT" : "ROUTINE", fechaVisita, solicitadoPorPaciente, ct);
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
                    _catalogs.Paneles.Where(p => p.PanelUuid == lab.CielUuid), categorias, _rng, dxUuids)
                : null;

            await _workflow.ProcesarOrdenAsync(patient, lab, orderUuid, result, componentes, ct);
        }

        _logger.LogInformation("[LabOrder] {N}/{Total} órdenes para {Id}",
            ordenesOk, elegidos.Count, patient.Identifier);
    }

    /// <summary>Crea la orden y devuelve su UUID (o null si falla).</summary>
    private async Task<string?> PostOrderAsync(
        SimulatedPatient patient, Models.Catalogs.LaboratorioEntry lab, string urgency,
        DateOnly fechaVisita, bool solicitadoPorPaciente, CancellationToken ct)
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
            commentToFulfiller = LabWorkflow.ComentarioAlLaboratorio(lab, solicitadoPorPaciente)
        };

        try
        {
            var json = await _client.PostAsync("order", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            return doc.TryGetProperty("uuid", out var uuid) ? uuid.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LabOrder] Error en orden para {Id}: {Msg}", patient.Identifier, ex.Message);
            return null;
        }
    }

}
