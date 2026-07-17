using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenmrsSeeder.Clients;
using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Seeders;

/// <summary>
/// Lleva la orden de laboratorio por su ciclo de vida real, que es lo que la app de laboratorio de O3
/// muestra en su cola: la muestra se <b>toma</b> (<c>IN_PROGRESS</c>), el resultado se registra y se
/// <b>valida</b> (<c>COMPLETED</c>), o la muestra se <b>rechaza</b> (<c>DECLINED</c>). Antes, el seeder
/// creaba la orden y no tocaba nunca <c>fulfillerStatus</c>: todas se quedaban eternamente en
/// "Tests ordered", sin tomar ni completar.
///
/// <para>Dos añadidos que cambian el resultado clínico:</para>
/// <list type="bullet">
/// <item><b>El resultado es un acto del laboratorio</b>: sus obs cuelgan de un encuentro propio
/// (tipo "Lab Results", ubicación Laboratorio, firmado por el técnico), no del encuentro de la consulta
/// firmado por el médico y fechado a la hora de la consulta.</item>
/// <item><b>Lo que la clínica no hace, se manda fuera</b> (<c>se_realiza_en_clinica=false</c>): el
/// resultado no llega ese día sino a los <c>dias_entrega_*</c>, y lo registra
/// <see cref="ProcesarEntregasDelDiaAsync"/> el día que llega — <b>sin</b> necesidad de que el paciente
/// vuelva (su encuentro de laboratorio no tiene visita: el paciente no está, la muestra sí).</item>
/// </list>
/// </summary>
public class LabWorkflowSeeder
{
    private readonly OpenMrsRestClient _client;
    private readonly OpenMrsSettings _settings;
    private readonly LabStaffAssigner _staff;
    private readonly LaboratorioSettings _lab;
    private readonly Random _rng;
    private readonly ILogger<LabWorkflowSeeder> _logger;

    /// <summary>Correlativo del nº de muestra (accessionNumber) dentro de la corrida.</summary>
    private int _secuenciaMuestra;

    public LabWorkflowSeeder(
        OpenMrsRestClient client,
        OpenMrsSettings settings,
        LabStaffAssigner staff,
        SimulationSettings simSettings,
        ILogger<LabWorkflowSeeder> logger)
    {
        _client   = client;
        _settings = settings;
        _staff    = staff;
        _lab      = simSettings.Laboratorio;
        _rng      = new Random(simSettings.RandomSeed + 23);
        _logger   = logger;
    }

    /// <summary>Nº de muestra de la próxima orden (lo estampa <c>LabOrderSeeder</c> al crearla).</summary>
    public string SiguienteNumeroMuestra(DateOnly fecha) =>
        LabWorkflow.NumeroMuestra(fecha, ++_secuenciaMuestra);

    /// <summary>
    /// Procesa una orden recién creada: decide su desenlace, la mueve por sus estados y —si el examen se
    /// hace en la clínica— registra el resultado el mismo día. Los externos quedan en
    /// <see cref="SimulatedPatient.ResultadosPendientes"/> con su fecha de entrega.
    /// </summary>
    public async Task ProcesarOrdenAsync(
        SimulatedPatient patient,
        LaboratorioEntry lab,
        string orderUuid,
        LabResultGenerator.LabResult result,
        List<(string ConceptUuid, double Valor)>? componentes,
        CancellationToken ct)
    {
        var tecnico    = _staff.Tecnico();
        var validador  = _staff.Validador(tecnico);
        var fechaOrden = DateOnly.FromDateTime(patient.VisitDatetime);

        // Un estudio de imagen no produce ningún valor registrable: vuelve el informe, no un número.
        var sinValor = result.Tipo == LabResultGenerator.TipoResultado.Ninguno &&
                       (componentes is null || componentes.Count == 0);

        var desenlace = LabWorkflow.DecidirDesenlace(
            _rng.NextDouble(), _rng.NextDouble(), _lab.ProbRechazo, _lab.ProbResultadoLlega);

        if (desenlace == LabWorkflow.Desenlace.MuestraRechazada)
        {
            await SetFulfillerAsync(orderUuid, LabWorkflow.Rechazada,
                LabWorkflow.MotivoRechazo(_rng.Next), patient.Identifier, ct);
            return;
        }

        // El laboratorio recoge la orden: entra en curso (es lo que la app llama "picked").
        await SetFulfillerAsync(orderUuid, LabWorkflow.EnProceso,
            LabWorkflow.ComentarioToma(tecnico?.Nombre ?? "el laboratorio", sinValor), patient.Identifier, ct);

        if (desenlace == LabWorkflow.Desenlace.ResultadoNuncaLlega) return; // se pierde: queda en curso

        var entrega = LabWorkflow.FechaEntrega(fechaOrden, lab, _rng.Next);

        // Se procesa fuera (o tarda días): el valor ya está decidido con el contexto clínico de HOY,
        // pero se registra el día que llega, en el barrido diario.
        if (entrega > fechaOrden)
        {
            patient.ResultadosPendientes.Add(new ResultadoPendiente(
                orderUuid, lab.CielUuid, result.Numerico, result.CodedUuid, componentes,
                entrega, EsExterno: !lab.SeRealizaEnClinica));
            return;
        }

        // Estudio sin valor registrable (imagen) que además se resuelve hoy: no hay obs que poner, solo
        // se cierra la orden (el informe va en papel al expediente).
        if (sinValor)
        {
            await SetFulfillerAsync(orderUuid, LabWorkflow.Completada,
                LabWorkflow.ComentarioEstudioExterno(), patient.Identifier, ct);
            return;
        }

        // Se hace aquí y sale hoy: la toma ocurre un rato después de la consulta, dentro de la visita.
        var momento = LabWorkflow.MomentoToma(
            ConsultaSeeder.FechaConsulta(patient),
            _rng.Next(_lab.MinutosHastaTomaMin, _lab.MinutosHastaTomaMax + 1));

        var encuentro = await CrearEncuentroLabAsync(
            patient, momento, tecnico?.ProviderUuid, patient.VisitUuid, ct);
        if (encuentro is null) return;

        // La visita no puede cerrarse antes de la toma (OpenMRS rechaza dejar fuera a un encuentro).
        if (patient.UltimoEncuentroDatetime is null || momento > patient.UltimoEncuentroDatetime)
            patient.UltimoEncuentroDatetime = momento;

        var ok = await RegistrarResultadoAsync(
            patient, lab.CielUuid, orderUuid, encuentro, result, componentes, momento, ct);

        if (ok)
            await SetFulfillerAsync(orderUuid, LabWorkflow.Completada,
                LabWorkflow.ComentarioValidacion(validador?.Nombre ?? "el laboratorio", externo: false),
                patient.Identifier, ct);
    }

    /// <summary>
    /// Barrido diario: registra los resultados cuya fecha de entrega ya llegó y cierra sus órdenes. Se
    /// llama una vez por día simulado, ANTES de las visitas del día.
    /// <para>
    /// El resultado se registra aunque el paciente no vuelva nunca: su encuentro de laboratorio va
    /// <b>sin visita</b> (la muestra se procesa sin el paciente delante). Es la diferencia con el
    /// comportamiento anterior, donde un resultado diferido solo se posteaba si había otra visita.
    /// </para>
    /// </summary>
    public async Task ProcesarEntregasDelDiaAsync(
        IEnumerable<SimulatedPatient> pool, DateOnly dia, CancellationToken ct)
    {
        var entregados = 0;

        foreach (var patient in pool)
        {
            if (ct.IsCancellationRequested) return;
            if (patient.ResultadosPendientes.Count == 0) continue;

            foreach (var p in patient.ResultadosPendientes.Where(p => p.FechaEntrega <= dia).ToList())
            {
                var tecnico   = _staff.Tecnico();
                var validador = _staff.Validador(tecnico);
                var momento   = dia.ToDateTime(new TimeOnly(_rng.Next(8, 16), _rng.Next(0, 60)));

                // Un estudio de imagen no registra valor: solo vuelve el informe → se cierra la orden.
                if (p.SinValor)
                {
                    await SetFulfillerAsync(p.OrderUuid, LabWorkflow.Completada,
                        LabWorkflow.ComentarioEstudioExterno(), patient.Identifier, ct);
                    patient.ResultadosPendientes.Remove(p);
                    entregados++;
                    continue;
                }

                var encuentro = await CrearEncuentroLabAsync(
                    patient, momento, tecnico?.ProviderUuid, visitUuid: null, ct);
                if (encuentro is null) continue; // se reintentará el día siguiente

                var result = p.Numerico is not null
                    ? new LabResultGenerator.LabResult(LabResultGenerator.TipoResultado.Numerico, p.Numerico, null)
                    : new LabResultGenerator.LabResult(LabResultGenerator.TipoResultado.Codificado, null, p.CodedUuid);

                var ok = await RegistrarResultadoAsync(
                    patient, p.ConceptUuid, p.OrderUuid, encuentro, result, p.Componentes, momento, ct);
                if (!ok) continue;

                await SetFulfillerAsync(p.OrderUuid, LabWorkflow.Completada,
                    LabWorkflow.ComentarioValidacion(validador?.Nombre ?? "el laboratorio", p.EsExterno),
                    patient.Identifier, ct);

                patient.ResultadosPendientes.Remove(p);
                entregados++;
            }
        }

        if (entregados > 0)
            _logger.LogInformation("[Lab] {N} resultado(s) entregados el {Dia}", entregados, dia);
    }

    // ── REST ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mueve el <c>fulfillerStatus</c> de la orden. ⚠️ El recurso es <c>POST order/{uuid}/fulfillerdetails</c>
    /// y la transición es <b>de ida</b>: mandar <c>null</c> no la revierte (el servicio ignora los nulos).
    /// </summary>
    private async Task SetFulfillerAsync(
        string orderUuid, string estado, string comentario, string identifier, CancellationToken ct)
    {
        try
        {
            await _client.PostAsync($"order/{orderUuid}/fulfillerdetails",
                new { fulfillerStatus = estado, fulfillerComment = comentario }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Lab] Error moviendo la orden {Order} a {Estado} para {Id}: {Msg}",
                orderUuid, estado, identifier, ex.Message);
        }
    }

    /// <summary>
    /// Encuentro del laboratorio. <paramref name="visitUuid"/> nulo = sin visita: el resultado que llega
    /// días después se procesa sin el paciente delante (OpenMRS lo admite y es lo realista).
    /// </summary>
    private async Task<string?> CrearEncuentroLabAsync(
        SimulatedPatient patient, DateTime momento, string? providerUuid, string? visitUuid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.Defaults.LabResultsEncounterTypeUuid))
            return null; // feature apagada

        var payload = ConstruirEncuentroLabPayload(
            _settings.Defaults.LabResultsEncounterTypeUuid,
            patient.OpenMrsUuid,
            VisitSeeder.FormatDatetime(momento),
            LabLocation(patient),
            providerUuid ?? patient.AssignedProviderUuid ?? _settings.Defaults.ProviderUuid,
            _settings.Defaults.EncounterRoleUuid,
            visitUuid);

        try
        {
            var json = await _client.PostAsync("encounter", payload, ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("uuid", out var uuid)) return uuid.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Lab] Error creando el encuentro de laboratorio de {Id}: {Msg}",
                patient.Identifier, ex.Message);
        }
        return null;
    }

    /// <summary>
    /// Payload del encuentro de laboratorio. Seam puro para poder afirmar sobre el JSON que sale por el
    /// cable: que lo firma el <b>técnico</b> en la ubicación <b>Laboratorio</b>, y que cuando el resultado
    /// llega días después va <b>sin visita</b> (la propiedad se omite, no se manda nula).
    /// </summary>
    public static object ConstruirEncuentroLabPayload(
        string encounterTypeUuid, string personUuid, string encounterDatetime,
        string locationUuid, string providerUuid, string encounterRoleUuid, string? visitUuid)
    {
        var providers = new[] { new { provider = providerUuid, encounterRole = encounterRoleUuid } };

        return string.IsNullOrWhiteSpace(visitUuid)
            ? new
            {
                encounterType      = encounterTypeUuid,
                patient            = personUuid,
                encounterDatetime,
                location           = locationUuid,
                encounterProviders = providers
            }
            : new
            {
                encounterType      = encounterTypeUuid,
                patient            = personUuid,
                encounterDatetime,
                location           = locationUuid,
                encounterProviders = providers,
                visit              = visitUuid
            };
    }

    private string LabLocation(SimulatedPatient patient) =>
        !string.IsNullOrWhiteSpace(_settings.Defaults.LabLocationUuid)
            ? _settings.Defaults.LabLocationUuid
            : patient.AssignedLocationUuid ?? _settings.Defaults.LocationUuid;

    /// <summary>Registra el resultado (simple o panel obs-group) ligado a la orden, en el encuentro del laboratorio.</summary>
    private async Task<bool> RegistrarResultadoAsync(
        SimulatedPatient patient, string conceptUuid, string orderUuid, string encounterUuid,
        LabResultGenerator.LabResult result, List<(string ConceptUuid, double Valor)>? componentes,
        DateTime momento, CancellationToken ct)
    {
        var fecha = VisitSeeder.FormatDatetime(momento);

        var payload = componentes is { Count: > 0 }
            ? ConstruirPanelPayload(conceptUuid, patient.OpenMrsUuid, encounterUuid, orderUuid, componentes, fecha)
            : ConstruirObsPayload(conceptUuid, patient.OpenMrsUuid, encounterUuid, orderUuid, result, fecha);

        if (payload is null) return false;

        try
        {
            await _client.PostAsync("obs", payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Lab] Error registrando el resultado {Concept} de {Id}: {Msg}",
                conceptUuid, patient.Identifier, ex.Message);
            return false;
        }
    }

    /// <summary>Obs de resultado simple (numérico o codificado) ligada a la orden. <c>null</c> = sin valor.</summary>
    public static object? ConstruirObsPayload(
        string conceptUuid, string personUuid, string encounterUuid, string orderUuid,
        LabResultGenerator.LabResult result, string obsDatetime)
    {
        object? value = result.Tipo switch
        {
            LabResultGenerator.TipoResultado.Numerico   => result.Numerico,
            LabResultGenerator.TipoResultado.Codificado => result.CodedUuid,
            _ => null
        };
        if (value is null) return null;

        return new
        {
            concept   = conceptUuid,
            person    = personUuid,
            encounter = encounterUuid,
            order     = orderUuid,
            obsDatetime,
            value
        };
    }

    /// <summary>
    /// Payload del resultado de un panel: obs padre (concepto del panel, ligada a la orden) + una obs hija
    /// por componente, en <c>groupMembers</c>.
    ///
    /// ⚠️ Cada hijo lleva su propio <c>encounter</c>: la REST API <b>no</b> propaga el del padre a los
    /// miembros del grupo. Sin él, los componentes nacen con <c>encounter_id</c> NULL y desaparecen de toda
    /// consulta por encuentro (el informe de la visita, cualquier ETL o export FHIR con contexto de visita),
    /// aunque el panel se siga viendo bien en la UI colgando de su padre.
    ///
    /// Los hijos NO llevan <c>order</c>: el resultado de la orden es el grupo, no cada miembro.
    ///
    /// Seam puro para poder afirmar sobre el JSON que de verdad va por el cable (<c>LabWorkflowSeederTests</c>).
    /// </summary>
    public static object ConstruirPanelPayload(
        string panelUuid, string personUuid, string encounterUuid, string orderUuid,
        IEnumerable<(string ConceptUuid, double Valor)> componentes, string obsDatetime) => new
    {
        concept     = panelUuid,
        person      = personUuid,
        encounter   = encounterUuid,
        order       = orderUuid,
        obsDatetime,
        groupMembers = componentes.Select(c => new
        {
            concept     = c.ConceptUuid,
            person      = personUuid,
            encounter   = encounterUuid,
            obsDatetime,
            value       = c.Valor
        }).ToArray()
    };
}
