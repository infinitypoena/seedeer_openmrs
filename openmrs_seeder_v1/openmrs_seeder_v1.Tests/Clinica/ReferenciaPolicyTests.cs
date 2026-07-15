using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests.Clinica;

public class ReferenciaPolicyTests
{
    private static DiagnosticoEntry Dx(
        string nombre, string severidad = "leve", string ambito = "clinica", bool cronica = false) =>
        new() { NombreEs = nombre, CielUuid = nombre, Severidad = severidad, Ambito = ambito, EsCronica = cronica };

    // ── Qué se refiere y qué no ────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoQueLaClinicaNoResuelve_SeRefiere()
    {
        var apendicitis = Dx("Apendicitis aguda", "grave", "referencia");

        Assert.True(ReferenciaPolicy.DebeReferir([apendicitis]));
    }

    [Fact]
    public void LoQueLaClinicaSiTrata_NoSeRefiere()
    {
        Assert.False(ReferenciaPolicy.DebeReferir([Dx("Faringitis aguda")]));
        Assert.False(ReferenciaPolicy.DebeReferir([]));
    }

    [Fact]
    public void UnCuadroGRAVE_NoEsMotivoSuficienteParaReferir()
    {
        // El matiz que decide si esto sale bien clínicamente: el primer nivel SÍ maneja muchas graves.
        // El VIH, la tuberculosis (DOTS) o el pie diabético son graves y tienen sus programas de
        // atención — referirlos al hospital sería un error. Lo que manda es la columna 'ambito'.
        var vih = Dx("VIH / SIDA", severidad: "grave", ambito: "clinica", cronica: true);
        var tb  = Dx("Tuberculosis pulmonar (BK+)", severidad: "grave", ambito: "clinica", cronica: true);

        Assert.False(ReferenciaPolicy.DebeReferir([vih]));
        Assert.False(ReferenciaPolicy.DebeReferir([tb, vih]));
    }

    [Fact]
    public void UnaComorbilidadQueSeRefiere_ArrastraTodaLaVisita()
    {
        // El paciente viene por su hipertensión y se le detecta una apendicitis: se va al hospital.
        var visita = new[]
        {
            Dx("Hipertensión arterial", cronica: true),
            Dx("Apendicitis aguda", "grave", "referencia")
        };

        Assert.True(ReferenciaPolicy.DebeReferir(visita));
    }

    // ── La prisa del traslado ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void LaSeveridadDecideLaPRISA_NoElTraslado()
    {
        var grave    = Dx("Sepsis", "grave", "referencia");
        var moderado = Dx("Hiperémesis gravídica", "moderado", "referencia");

        Assert.Equal(ReferenciaPolicy.PrioridadEmergenciaUuid, ReferenciaPolicy.Prioridad([grave]));
        Assert.Equal(ReferenciaPolicy.PrioridadUrgenteUuid,    ReferenciaPolicy.Prioridad([moderado]));
    }

    [Fact]
    public void LaPrioridadLaFijaElCuadroQueSEREFIERE_NoOtroCualquiera()
    {
        // Una comorbilidad crónica grave (que NO se refiere) no debe convertir en emergencia el traslado
        // de un cuadro que solo es urgente.
        var visita = new[]
        {
            Dx("VIH / SIDA", "grave", "clinica", cronica: true),
            Dx("Hiperémesis gravídica", "moderado", "referencia")
        };

        Assert.Equal(ReferenciaPolicy.PrioridadUrgenteUuid, ReferenciaPolicy.Prioridad(visita));
    }

    // ── El motivo que se escribe en la referencia ──────────────────────────────────────────────────

    [Fact]
    public void ElMotivo_NombraSoloLosCuadrosQueJustificanElTraslado()
    {
        var visita = new[]
        {
            Dx("Diabetes mellitus tipo 2", cronica: true),
            Dx("Cetoacidosis diabética", "grave", "referencia")
        };

        var motivo = ReferenciaPolicy.Motivo(visita);

        Assert.Contains("Cetoacidosis diabética", motivo);
        Assert.DoesNotContain("Diabetes mellitus tipo 2", motivo);
        Assert.Contains("segundo nivel", motivo);
    }

    [Fact]
    public void SinReferencia_NoHayMotivo()
    {
        Assert.Empty(ReferenciaPolicy.Motivo([Dx("Faringitis aguda")]));
    }

    // ── El control post-alta ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void AlReferido_SeLeCitaCasiSiempre_ParaVerloTrasElAlta()
    {
        var rp = new ReferralProbabilitiesSettings();
        var apendicitis = new[] { Dx("Apendicitis aguda", "grave", "referencia") };

        Assert.Equal(rp.FollowUpReferido, SeguimientoPolicy.Probabilidad(apendicitis, rp, referido: true));
        // Y gana sobre las demás ramas (crónico/grave/base).
        Assert.True(rp.FollowUpReferido >= rp.FollowUpCronico);
    }

    [Fact]
    public void ElControlPostAlta_CaeEnSuPropiaBanda_NiLaAgudaNiLaCronica()
    {
        var s = new RecurrenceSettings();   // agudo 7-21 · crónico 30-120 · post-referencia 15-30
        var hoy = new DateOnly(2024, 6, 10);

        var fechas = Enumerable.Range(0, 200)
            .Select(i => RecurrenceScheduler.ProximaFechaPostReferencia(hoy, new Random(i), s))
            .Select(f => f.DayNumber - hoy.DayNumber)
            .ToList();

        Assert.All(fechas, d => Assert.InRange(d, s.MinDiasPostReferencia, s.MaxDiasPostReferencia));
        // Con la banda aguda (7-21 d) el paciente aún estaría ingresado; con la crónica (30-120) se vería
        // demasiado tarde cómo salió del hospital.
        Assert.All(fechas, d => Assert.True(d > s.MinDiasAgudo));
        Assert.All(fechas, d => Assert.True(d <= s.MinDiasCronico));
    }
}
