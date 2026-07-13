using System.Text.Json;
using OpenmrsSeeder.Seeders;

namespace openmrs_seeder_v1.Tests;

/// <summary>
/// El resultado de un panel (hoy el hemograma) va como obs-group: una obs padre con el concepto del panel y
/// una obs hija por componente. Los tests afirman sobre el JSON que de verdad sale por el cable, porque el
/// fallo que motivó este fichero vivía justo ahí: el payload se construía inline dentro del método async, así
/// que ningún test podía verlo y las 5.464 obs hijas nacieron sin encuentro sin que nada chillara.
/// </summary>
public class LabOrderSeederTests
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
        LabOrderSeeder.ConstruirPanelPayload(Panel, Persona, Encuentro, Orden, Componentes, Fecha));

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
}
