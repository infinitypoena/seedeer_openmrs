using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;

namespace openmrs_seeder_v1.Tests.Clinica;

public class ConsultaSeederTests
{
    [Fact]
    public void ValorExamenNumerico_BandaEntera_DevuelveEnteroDentroDeLaBanda()
    {
        // Glasgow 12-15: límites enteros → valor entero (los conceptos de puntaje no admiten decimales)
        var examen = new ExamenClinicoEntry { ResMin = 12, ResMax = 15 };
        var rng = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            var v = ConsultaSeeder.ValorExamenNumerico(examen, rng);
            Assert.InRange(v, 12, 15);
            Assert.Equal(v, Math.Round(v)); // sin decimales
        }
    }

    [Fact]
    public void ValorExamenNumerico_BandaDecimal_ConservaUnDecimal()
    {
        var examen = new ExamenClinicoEntry { ResMin = 0.5, ResMax = 1.3 };
        var rng = new Random(42);

        for (int i = 0; i < 50; i++)
        {
            var v = ConsultaSeeder.ValorExamenNumerico(examen, rng);
            Assert.InRange(v, 0.5, 1.3);
        }
    }

    // ══ DxsEvaluables — el corte del bucle de referencias (ley L9) ═══════════════════════════════

    [Fact]
    public void DxsEvaluables_EpisodioNuevo_DevuelveTodosLosDx_YRefiere()
    {
        // Primera visita con apendicitis: el episodio es nuevo — se evalúa entero y se refiere.
        var apendicitis = Factorias.Dx(uuid: "apendicitis", severidad: "grave", ambito: "referencia");
        var dm2         = Factorias.Dx(uuid: "dm2", cronica: true, categoria: "diabetes");
        var p = new SimulatedPatient { Diagnostico = apendicitis, Comorbilidades = [dm2] };

        var dxs = ConsultaSeeder.DxsEvaluables(p);

        Assert.Equal(2, dxs.Count);
        Assert.True(ReferenciaPolicy.DebeReferir(dxs));
    }

    [Fact]
    public void DxsEvaluables_ControlPostAlta_ExcluyeElEpisodioResuelto_YNoReRefiere()
    {
        // El control post-alta: el hospital ya resolvió la apendicitis. Queda fuera de la decisión →
        // sin re-remisión (máx. 1 control por episodio), y el seguimiento se decide por lo que quede.
        var apendicitis = Factorias.Dx(uuid: "apendicitis", severidad: "grave", ambito: "referencia");
        var dm2         = Factorias.Dx(uuid: "dm2", cronica: true, categoria: "diabetes");
        var p = new SimulatedPatient
        {
            Diagnostico = apendicitis,
            Comorbilidades = [dm2],
            EsControlPostReferencia = true,
        };

        var dxs = ConsultaSeeder.DxsEvaluables(p);

        Assert.Single(dxs);
        Assert.Equal("dm2", dxs[0].CielUuid);
        Assert.False(ReferenciaPolicy.DebeReferir(dxs));
    }

    [Fact]
    public void DxsEvaluables_ComorbilidadDeReferenciaFrescaEnUnControl_SiRefiere()
    {
        // En el control de la apendicitis aparece OTRO cuadro de referencia (colecistitis): es un
        // episodio nuevo y se refiere, aunque la visita sea un control.
        var apendicitis  = Factorias.Dx(uuid: "apendicitis", severidad: "grave", ambito: "referencia");
        var colecistitis = Factorias.Dx(uuid: "colecistitis", severidad: "grave", ambito: "referencia");
        var p = new SimulatedPatient
        {
            Diagnostico = apendicitis,
            Comorbilidades = [colecistitis],
            EsControlPostReferencia = true,
        };

        var dxs = ConsultaSeeder.DxsEvaluables(p);

        Assert.Single(dxs);
        Assert.True(ReferenciaPolicy.DebeReferir(dxs));
    }

    [Fact]
    public void DxsEvaluables_ControlConPrimarioNoReferencia_NoExcluyeNada()
    {
        // El flag sin un dx de referencia como primario (control crónico normal) no recorta la lista.
        var dm2 = Factorias.Dx(uuid: "dm2", cronica: true, categoria: "diabetes");
        var p = new SimulatedPatient { Diagnostico = dm2, EsControlPostReferencia = true };

        Assert.Single(ConsultaSeeder.DxsEvaluables(p));
    }
}
