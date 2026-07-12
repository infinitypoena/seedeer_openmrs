using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class RecurrentSelectorTests
{
    private static readonly DateOnly Hoy = new(2024, 6, 10);

    private static SimulatedPatient P(string uuid, DateOnly? cita = null) =>
        new() { OpenMrsUuid = uuid, ProximaCita = cita };

    [Fact]
    public void CitaParaHoy_SePriorizaSobreLosSinCita()
    {
        var conCita = P("cita", Hoy);
        var elegibles = new List<SimulatedPatient> { P("a"), P("b"), conCita, P("c") };

        // Cupo 1, asistencia segura → debe salir el de la cita, no un aleatorio.
        var sel = RecurrentSelector.Seleccionar(elegibles, Hoy, cupo: 1, toleranciaDias: 3, asistenciaProb: 1.0, new Random(1));

        Assert.Single(sel);
        Assert.Equal("cita", sel[0].OpenMrsUuid);
    }

    [Fact]
    public void CitaDentroDeTolerancia_Cuenta_FueraDeTolerancia_No()
    {
        var dentro = P("dentro", Hoy.AddDays(3));   // dentro de ±3
        var fuera  = P("fuera",  Hoy.AddDays(10));  // fuera de tolerancia (aún no vencida como elegible igual)
        var elegibles = new List<SimulatedPatient> { fuera, dentro };

        var sel = RecurrentSelector.Seleccionar(elegibles, Hoy, cupo: 1, toleranciaDias: 3, asistenciaProb: 1.0, new Random(2));

        Assert.Single(sel);
        Assert.Equal("dentro", sel[0].OpenMrsUuid);
    }

    [Fact]
    public void NoShow_LiberaCupoParaRelleno()
    {
        var conCita = P("cita", Hoy);
        var relleno = P("relleno");
        var elegibles = new List<SimulatedPatient> { conCita, relleno };

        // Asistencia 0 → el de la cita no asiste; el cupo se rellena con el otro elegible.
        var sel = RecurrentSelector.Seleccionar(elegibles, Hoy, cupo: 1, toleranciaDias: 3, asistenciaProb: 0.0, new Random(3));

        Assert.Single(sel);
        Assert.Equal("relleno", sel[0].OpenMrsUuid);
    }

    [Fact]
    public void RespetaCupo_YNoRepite()
    {
        var elegibles = new List<SimulatedPatient>
        {
            P("a", Hoy), P("b", Hoy), P("c"), P("d")
        };

        var sel = RecurrentSelector.Seleccionar(elegibles, Hoy, cupo: 3, toleranciaDias: 3, asistenciaProb: 1.0, new Random(4));

        Assert.Equal(3, sel.Count);
        Assert.Equal(3, sel.Select(p => p.OpenMrsUuid).Distinct().Count());
        // Los dos con cita entran; el tercero sale del relleno.
        Assert.Contains(sel, p => p.OpenMrsUuid == "a");
        Assert.Contains(sel, p => p.OpenMrsUuid == "b");
    }

    [Fact]
    public void CupoCeroOSinElegibles_DevuelveVacio()
    {
        Assert.Empty(RecurrentSelector.Seleccionar([P("a", Hoy)], Hoy, 0, 3, 1.0, new Random(5)));
        Assert.Empty(RecurrentSelector.Seleccionar([], Hoy, 5, 3, 1.0, new Random(6)));
    }

    // ── TieneCitaHoy (mismo criterio que usa el orquestador para el médico y el motivo) ──

    [Theory]
    [InlineData(0, true)]    // la cita es hoy
    [InlineData(-3, true)]   // llega 3 días tarde (límite de tolerancia)
    [InlineData(3, true)]    // se adelanta 3 días
    [InlineData(-4, false)]  // fuera de tolerancia: ya no es "su cita"
    [InlineData(4, false)]
    public void TieneCitaHoy_RespetaLaTolerancia(int offsetDias, bool esperado)
    {
        var p = P("x", Hoy.AddDays(offsetDias));
        Assert.Equal(esperado, RecurrentSelector.TieneCitaHoy(p, Hoy, toleranciaDias: 3));
    }

    [Fact]
    public void TieneCitaHoy_SinCita_EsFalso()
    {
        Assert.False(RecurrentSelector.TieneCitaHoy(P("x"), Hoy, toleranciaDias: 3));
    }
}
