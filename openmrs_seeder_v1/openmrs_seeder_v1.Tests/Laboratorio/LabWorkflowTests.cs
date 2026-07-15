using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Seeders;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests.Laboratorio;

/// <summary>
/// Decisiones del ciclo de vida de la orden: qué le pasa a la muestra y cuándo llega el resultado.
/// Todo con las tiradas inyectadas, sin red.
/// </summary>
public class LabWorkflowTests
{
    private static LaboratorioEntry Lab(bool enClinica, int min = 0, int max = 0) => new()
    {
        CielUuid = "uuid-lab",
        NombreEs = "Examen",
        SeRealizaEnClinica = enClinica,
        DiasEntregaMin = min,
        DiasEntregaMax = max
    };

    // ── Desenlace ─────────────────────────────────────────────────────────────

    [Fact]
    public void UnaTiradaBajaRechazaLaMuestra()
    {
        var d = LabWorkflow.DecidirDesenlace(rollRechazo: 0.01, rollLlega: 0.5, probRechazo: 0.04, probResultadoLlega: 0.95);
        Assert.Equal(LabWorkflow.Desenlace.MuestraRechazada, d);
    }

    [Fact]
    public void LoNormalEsQueLaMuestraSeProcese()
    {
        var d = LabWorkflow.DecidirDesenlace(rollRechazo: 0.50, rollLlega: 0.50, probRechazo: 0.04, probResultadoLlega: 0.95);
        Assert.Equal(LabWorkflow.Desenlace.Procesada, d);
    }

    [Fact]
    public void AVecesElResultadoSePierde_YLaOrdenSeQuedaEnCurso()
    {
        var d = LabWorkflow.DecidirDesenlace(rollRechazo: 0.50, rollLlega: 0.99, probRechazo: 0.04, probResultadoLlega: 0.95);
        Assert.Equal(LabWorkflow.Desenlace.ResultadoNuncaLlega, d);
    }

    [Fact]
    public void ConProbabilidadesEnCero_NuncaSeRechaza()
    {
        var d = LabWorkflow.DecidirDesenlace(rollRechazo: 0.0, rollLlega: 0.0, probRechazo: 0.0, probResultadoLlega: 1.0);
        Assert.Equal(LabWorkflow.Desenlace.Procesada, d);
    }

    // ── Fecha de entrega ──────────────────────────────────────────────────────

    [Fact]
    public void LoQueSeHaceEnLaClinicaSaleElMismoDia()
    {
        var orden = new DateOnly(2025, 3, 14);
        var entrega = LabWorkflow.FechaEntrega(orden, Lab(enClinica: true), (lo, hi) => lo);

        Assert.Equal(orden, entrega);
    }

    [Fact]
    public void LoQueSeMandaFuera_TardaLosDiasDelCatalogo()
    {
        var orden = new DateOnly(2025, 3, 14);
        // Random.Next(min, max+1) → el máximo de la banda 2-5 es 5
        var entrega = LabWorkflow.FechaEntrega(orden, Lab(enClinica: false, 2, 5), (lo, hi) => hi - 1);

        Assert.Equal(orden.AddDays(5), entrega);
        Assert.True(entrega > orden);
    }

    [Fact]
    public void UnaBandaInvertidaNoAdelantaLaEntrega()
    {
        var orden = new DateOnly(2025, 3, 14);
        var entrega = LabWorkflow.FechaEntrega(orden, Lab(enClinica: false, 7, 2), (lo, hi) => lo);

        Assert.Equal(orden.AddDays(7), entrega);
    }

    // ── Comentarios y nº de muestra ───────────────────────────────────────────

    [Fact]
    public void LaOrdenLlevaLaInstruccionQueCorresponde()
    {
        Assert.Contains("clínica", LabWorkflow.ComentarioAlLaboratorio(Lab(enClinica: true)));
        Assert.Contains("externo", LabWorkflow.ComentarioAlLaboratorio(Lab(enClinica: false)));
    }

    [Fact]
    public void ElNumeroDeMuestraLlevaLaFechaYUnCorrelativo()
    {
        Assert.Equal("LAB-20250314-0007", LabWorkflow.NumeroMuestra(new DateOnly(2025, 3, 14), 7));
    }

    [Fact]
    public void LaValidacionDistingueSiElResultadoVinoDeFuera()
    {
        Assert.Contains("externo", LabWorkflow.ComentarioValidacion("Silvia", externo: true));
        Assert.DoesNotContain("externo", LabWorkflow.ComentarioValidacion("Silvia", externo: false));
    }

    [Fact]
    public void EnUnaImagenNoHayMuestraQueTomar()
    {
        // Una radiografía no se "toma" como una muestra: al paciente se le remite al centro de imágenes.
        Assert.Contains("Muestra tomada", LabWorkflow.ComentarioToma("Mario", esImagen: false));
        Assert.DoesNotContain("Muestra", LabWorkflow.ComentarioToma("Mario", esImagen: true));
    }

    [Fact]
    public void LaTomaOcurreDespuesDeLaConsulta_NoALaVez()
    {
        var consulta = new DateTime(2025, 3, 14, 9, 30, 0);
        Assert.Equal(consulta.AddMinutes(45), LabWorkflow.MomentoToma(consulta, 45));
        // Aunque se pidan 0 minutos, la toma nunca coincide exactamente con la consulta
        Assert.True(LabWorkflow.MomentoToma(consulta, 0) > consulta);
    }

    // ── Personal del laboratorio ──────────────────────────────────────────────

    [Fact]
    public void SiUnaPersonaDelCatalogoNoSePuedeAsegurar_LaCorridaAborta()
    {
        // Fail-fast: mejor abortar antes de sembrar que dejar encuentros firmados por nadie.
        var resueltos = new List<(PersonalLaboratorioEntry, string?)>
        {
            (new PersonalLaboratorioEntry { Identifier = "SIM-LAB-01", Rol = "tecnico" }, "uuid-1"),
            (new PersonalLaboratorioEntry { Identifier = "SIM-LAB-02", Rol = "tecnico" }, null)
        };

        var ex = Assert.Throws<InvalidOperationException>(() => LabStaffAssigner.ResolvePool(resueltos));
        Assert.Contains("SIM-LAB-02", ex.Message);
    }

    [Fact]
    public void SinCatalogo_NoLanza_YLaFeatureQuedaApagada()
    {
        Assert.Empty(LabStaffAssigner.ResolvePool([]));
    }

    [Fact]
    public void ElResponsableSeDistingueDelTecnico()
    {
        var pool = LabStaffAssigner.ResolvePool(
        [
            (new PersonalLaboratorioEntry { Identifier = "SIM-LAB-01", Nombre = "Ana",    Rol = "tecnico" },     "uuid-1"),
            (new PersonalLaboratorioEntry { Identifier = "SIM-LAB-03", Nombre = "Silvia", Rol = "responsable" }, "uuid-3")
        ]);

        Assert.Equal("Silvia", Assert.Single(pool, s => s.EsResponsable).Nombre);
        Assert.Equal("Ana", Assert.Single(pool, s => !s.EsResponsable).Nombre);
    }

    // ── Cierre de la visita ───────────────────────────────────────────────────

    [Fact]
    public void LaVisitaNoSeCierraAntesDeLaTomaDeMuestra()
    {
        // OpenMRS rechaza cerrar una visita dejando fuera a uno de sus encuentros. La toma ocurre después
        // de la consulta y puede caer más tarde que la duración sorteada de la visita.
        var llegada = new DateTime(2025, 3, 14, 9, 0, 0);
        var toma    = llegada.AddMinutes(120);

        var cierre = VisitCloseSeeder.HoraCierre(llegada, duracionMinutos: 60, ultimoEncuentro: toma);

        Assert.True(cierre > toma);
    }

    [Fact]
    public void SinEncuentrosTardios_ElCierreEsElDeSiempre()
    {
        var llegada = new DateTime(2025, 3, 14, 9, 0, 0);

        Assert.Equal(llegada.AddMinutes(90), VisitCloseSeeder.HoraCierre(llegada, 90, null));
        Assert.Equal(llegada.AddMinutes(90), VisitCloseSeeder.HoraCierre(llegada, 90, llegada.AddMinutes(30)));
    }
}
