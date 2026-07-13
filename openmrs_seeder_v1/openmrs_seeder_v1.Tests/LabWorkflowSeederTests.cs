using System.Text.Json;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests;

/// <summary>
/// Los payloads del laboratorio, afirmados sobre el JSON que de verdad sale por el cable — porque el fallo
/// que motivó estos tests vivía justo ahí: el payload del panel se construía inline dentro de un método
/// async, así que ningún test podía verlo y las 5.464 obs hijas nacieron sin encuentro sin que nada chillara.
/// </summary>
public class LabWorkflowSeederTests
{
    private const string Panel     = "1019AAAA-panel";
    private const string Persona   = "uuid-persona";
    private const string Encuentro = "uuid-encuentro";
    private const string Orden     = "uuid-orden";
    private const string Fecha     = "2025-03-14T09:30:00.000-06:00";

    private static readonly (string ConceptUuid, double Valor)[] Componentes =
    [
        ("uuid-hemoglobina", 13.1),
        ("uuid-hematocrito", 40.2),
        ("uuid-leucocitos",  8.4),
        ("uuid-plaquetas",   250)
    ];

    private static JsonElement Payload() => JsonSerializer.SerializeToElement(
        LabWorkflowSeeder.ConstruirPanelPayload(Panel, Persona, Encuentro, Orden, Componentes, Fecha));

    // ── Panel (obs-group) ─────────────────────────────────────────────────────

    [Fact]
    public void ElPadreLlevaConceptoDelPanel_EncuentroYOrden()
    {
        var payload = Payload();

        Assert.Equal(Panel, payload.GetProperty("concept").GetString());
        Assert.Equal(Persona, payload.GetProperty("person").GetString());
        Assert.Equal(Encuentro, payload.GetProperty("encounter").GetString());
        Assert.Equal(Orden, payload.GetProperty("order").GetString());
        Assert.Equal(Fecha, payload.GetProperty("obsDatetime").GetString());
    }

    [Fact]
    public void CadaHijaLlevaSuEncuentro()
    {
        // ⚠️ La razón de ser de este test: la REST API NO propaga el encounter del padre a los groupMembers.
        // Sin él, los componentes nacen con encounter_id NULL y desaparecen de toda consulta por encuentro.
        var hijas = Payload().GetProperty("groupMembers").EnumerateArray().ToList();

        Assert.Equal(Componentes.Length, hijas.Count);
        Assert.All(hijas, h => Assert.Equal(Encuentro, h.GetProperty("encounter").GetString()));
    }

    [Fact]
    public void CadaHijaLlevaSuConceptoYSuValor()
    {
        var hijas = Payload().GetProperty("groupMembers").EnumerateArray().ToList();

        foreach (var (conceptUuid, valor) in Componentes)
        {
            var hija = Assert.Single(hijas, h => h.GetProperty("concept").GetString() == conceptUuid);
            Assert.Equal(valor, hija.GetProperty("value").GetDouble());
            Assert.Equal(Persona, hija.GetProperty("person").GetString());
            Assert.Equal(Fecha, hija.GetProperty("obsDatetime").GetString());
        }
    }

    [Fact]
    public void LasHijasNoLlevanOrden()
    {
        // El resultado de la orden es el grupo (el padre), no cada miembro
        var hijas = Payload().GetProperty("groupMembers").EnumerateArray();

        Assert.All(hijas, h => Assert.False(h.TryGetProperty("order", out _)));
    }

    // ── Encuentro del laboratorio ─────────────────────────────────────────────

    private const string TipoLab   = "uuid-tipo-lab-results";
    private const string Laborator = "uuid-ubicacion-laboratorio";
    private const string Tecnico   = "uuid-tecnico";
    private const string Rol       = "uuid-rol-clinician";

    private static JsonElement Encuentro_(string? visita) => JsonSerializer.SerializeToElement(
        LabWorkflowSeeder.ConstruirEncuentroLabPayload(
            TipoLab, Persona, Fecha, Laborator, Tecnico, Rol, visita));

    [Fact]
    public void ElEncuentroDelLaboratorioLoFirmaElTecnicoEnElLaboratorio()
    {
        // El acto es del laboratorio: ni lo firma el médico de la consulta ni ocurre en su consultorio.
        var e = Encuentro_("uuid-visita");

        Assert.Equal(TipoLab, e.GetProperty("encounterType").GetString());
        Assert.Equal(Laborator, e.GetProperty("location").GetString());
        Assert.Equal(Fecha, e.GetProperty("encounterDatetime").GetString());

        var provider = e.GetProperty("encounterProviders").EnumerateArray().Single();
        Assert.Equal(Tecnico, provider.GetProperty("provider").GetString());
        Assert.Equal(Rol, provider.GetProperty("encounterRole").GetString());
    }

    [Fact]
    public void ElResultadoDelMismoDiaCuelgaDeLaVisita()
    {
        Assert.Equal("uuid-visita", Encuentro_("uuid-visita").GetProperty("visit").GetString());
    }

    [Fact]
    public void ElResultadoQueLlegaDespuesVaSinVisita()
    {
        // La muestra del laboratorio externo se procesa días después, sin el paciente delante: no hay
        // visita a la que colgar el encuentro. La propiedad se OMITE (no se manda nula: OpenMRS la rechaza).
        foreach (var sinVisita in new[] { (string?)null, "" })
            Assert.False(Encuentro_(sinVisita).TryGetProperty("visit", out _));
    }

    // ── Obs de resultado simple ───────────────────────────────────────────────

    [Fact]
    public void LaObsDeResultadoVaLigadaALaOrdenYAlEncuentroDelLaboratorio()
    {
        var result = new LabResultGenerator.LabResult(LabResultGenerator.TipoResultado.Numerico, 5.4, null);
        var obs = JsonSerializer.SerializeToElement(
            LabWorkflowSeeder.ConstruirObsPayload("uuid-hba1c", Persona, Encuentro, Orden, result, Fecha)!);

        Assert.Equal("uuid-hba1c", obs.GetProperty("concept").GetString());
        Assert.Equal(Orden, obs.GetProperty("order").GetString());
        Assert.Equal(Encuentro, obs.GetProperty("encounter").GetString());
        Assert.Equal(5.4, obs.GetProperty("value").GetDouble());
    }

    [Fact]
    public void UnResultadoCodificadoMandaElUuidDeLaRespuesta()
    {
        var result = new LabResultGenerator.LabResult(
            LabResultGenerator.TipoResultado.Codificado, null, "uuid-positivo");
        var obs = JsonSerializer.SerializeToElement(
            LabWorkflowSeeder.ConstruirObsPayload("uuid-ns1", Persona, Encuentro, Orden, result, Fecha)!);

        Assert.Equal("uuid-positivo", obs.GetProperty("value").GetString());
    }

    [Fact]
    public void UnEstudioSinValor_NoProduceObs()
    {
        // Una imagen se ordena y se cierra, pero no registra ningún valor: el informe va en papel.
        Assert.Null(LabWorkflowSeeder.ConstruirObsPayload(
            "uuid-radiografia", Persona, Encuentro, Orden, LabResultGenerator.LabResult.Ninguno, Fecha));
    }
}
