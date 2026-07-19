using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Clinica;

/// <summary>
/// La certeza del diagnóstico deja de ser un 70 % al azar: se condiciona a si el cuadro tiene examen
/// confirmatorio en el catálogo y a dónde se procesa (interno = resultado en la misma visita →
/// CONFIRMED; externo = días de espera → PROVISIONAL), y una visita de control no re-sospecha lo que
/// ya se estudió.
/// </summary>
public class CertaintyPolicyTests
{
    private static DiagnosticoEntry Dx(string uuid = "dengue") =>
        new() { CielUuid = uuid, NombreEs = uuid, Categoria = "infeccioso", Severidad = "leve" };

    private static IReadOnlyDictionary<string, IReadOnlyList<LaboratorioEntry>> Indice(
        string dxUuid, params bool[] internos) =>
        new Dictionary<string, IReadOnlyList<LaboratorioEntry>>
        {
            [dxUuid] = internos
                .Select((interno, i) => new LaboratorioEntry
                    { CielUuid = $"lab{i}", SeRealizaEnClinica = interno })
                .ToList()
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<LaboratorioEntry>> SinConfirmatorios =
        new Dictionary<string, IReadOnlyList<LaboratorioEntry>>();

    [Fact]
    public void ConfirmatorioInterno_SiempreConfirmed()
    {
        for (int seed = 0; seed < 100; seed++)
            Assert.Equal("CONFIRMED", CertaintyPolicy.Certainty(
                Dx(), Indice("dengue", internos: true), esVisitaControl: false, new Random(seed)));
    }

    [Fact]
    public void ConfirmatorioExterno_SiempreProvisional()
    {
        // El resultado tarda días en llegar: en la consulta el dx queda pendiente de confirmación.
        for (int seed = 0; seed < 100; seed++)
            Assert.Equal("PROVISIONAL", CertaintyPolicy.Certainty(
                Dx("vih"), Indice("vih", internos: false), esVisitaControl: false, new Random(seed)));
    }

    [Fact]
    public void ConfirmatorioMixto_BastaUnoInternoParaConfirmed()
    {
        // ITU: orina (interna, mismo día) + urocultivo (externo) → la orina ya confirma.
        Assert.Equal("CONFIRMED", CertaintyPolicy.Certainty(
            Dx("itu"), Indice("itu", internos: [false, true]), esVisitaControl: false, Azar.Rng()));
    }

    [Fact]
    public void VisitaDeControl_SiempreConfirmed_InclusoConConfirmatorioExterno()
    {
        // El cuadro ya se estudió en el episodio anterior: el control no re-sospecha.
        for (int seed = 0; seed < 100; seed++)
            Assert.Equal("CONFIRMED", CertaintyPolicy.Certainty(
                Dx("vih"), Indice("vih", internos: false), esVisitaControl: true, new Random(seed)));
    }

    [Fact]
    public void SinConfirmatorio_TiradaHistoricaDel70()
    {
        int confirmados = 0;
        const int N = 2000;
        var rng = Azar.Rng();
        for (int i = 0; i < N; i++)
            if (CertaintyPolicy.Certainty(Dx("gripe"), SinConfirmatorios, false, rng) == "CONFIRMED")
                confirmados++;
        Assert.InRange(confirmados / (double)N, 0.65, 0.75);
    }
}
