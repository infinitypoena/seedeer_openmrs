using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Seeders;
using Xunit;

namespace openmrs_seeder_v1.Tests;

/// <summary>
/// Motivo de la visita de un recurrente (<see cref="SeedOrchestrator.DxDeControl"/>): la cita manda —
/// quien acude a su control vuelve por el mismo cuadro por el que se le citó; sin cita, se conserva la
/// continuidad probabilística (crónica / episodio agudo abierto).
/// </summary>
public class SeedOrchestratorTests
{
    private static readonly DateOnly Hoy = new(2024, 6, 10);

    private static DiagnosticoEntry Dx(string uuid, bool cronica = false, string categoria = "respiratorio") =>
        new() { CielUuid = uuid, NombreEs = uuid, Categoria = categoria, EsCronica = cronica };

    private static (DiagnosticoEntry? Dx, bool Cronico, bool Agudo) Decidir(
        SimulatedPatient p, bool rollCronico = true, bool rollAgudo = true, int nextInt = 0) =>
        SeedOrchestrator.DxDeControl(
            p, Hoy, toleranciaDias: 3, ventanaAgudoDias: 30,
            () => rollCronico, () => rollAgudo, _ => nextInt);

    [Fact]
    public void ConCitaHoy_ElMotivoDeLaCitaEsElDiagnostico()
    {
        var motivo = Dx("dengue");
        var p = new SimulatedPatient
        {
            ProximaCita = Hoy,
            MotivoProximaCita = motivo,
            // Aunque arrastre crónicas y un agudo abierto, la cita manda: viene por lo que se le citó.
            CronicasActivas = [Dx("hta", cronica: true)],
            UltimoDxAgudo = Dx("gripe"),
            FechaUltimoDxAgudo = Hoy.AddDays(-5)
        };

        var (dx, cronico, agudo) = Decidir(p);

        Assert.Same(motivo, dx);
        Assert.False(cronico);
        Assert.True(agudo);   // motivo no crónico → la visita cierra el episodio agudo
    }

    [Fact]
    public void ConCitaHoy_MotivoCronico_EsControlCronico()
    {
        var motivo = Dx("dm2", cronica: true, categoria: "diabetes");
        var p = new SimulatedPatient { ProximaCita = Hoy, MotivoProximaCita = motivo };

        var (dx, cronico, agudo) = Decidir(p);

        Assert.Same(motivo, dx);
        Assert.True(cronico);
        Assert.False(agudo);
    }

    [Fact]
    public void CitaFueraDeTolerancia_NoManda_CaeALaLogicaProbabilistica()
    {
        var motivo = Dx("dengue");
        var cronica = Dx("hta", cronica: true, categoria: "cardiovascular");
        var p = new SimulatedPatient
        {
            ProximaCita = Hoy.AddDays(-10),   // llegó muy tarde: ya no es "su cita"
            MotivoProximaCita = motivo,
            CronicasActivas = [cronica]
        };

        var (dx, cronico, _) = Decidir(p, rollCronico: true);

        Assert.Same(cronica, dx);
        Assert.True(cronico);
    }

    [Fact]
    public void SinCita_ConCronica_YRollFavorable_EsControlCronico()
    {
        var cronica = Dx("epoc", cronica: true);
        var p = new SimulatedPatient { CronicasActivas = [cronica] };

        var (dx, cronico, agudo) = Decidir(p, rollCronico: true);

        Assert.Same(cronica, dx);
        Assert.True(cronico);
        Assert.False(agudo);
    }

    [Fact]
    public void SinCita_EpisodioAgudoVigente_YRollFavorable_VuelvePorElMismoDx()
    {
        var agudoDx = Dx("neumonia");
        var p = new SimulatedPatient
        {
            UltimoDxAgudo = agudoDx,
            FechaUltimoDxAgudo = Hoy.AddDays(-10)   // dentro de la ventana de 30 d
        };

        var (dx, cronico, agudo) = Decidir(p, rollCronico: false, rollAgudo: true);

        Assert.Same(agudoDx, dx);
        Assert.False(cronico);
        Assert.True(agudo);
    }

    [Fact]
    public void SinCita_EpisodioAgudoExpirado_NoLoReutiliza()
    {
        var p = new SimulatedPatient
        {
            UltimoDxAgudo = Dx("neumonia"),
            FechaUltimoDxAgudo = Hoy.AddDays(-45)   // fuera de la ventana de 30 d
        };

        var (dx, cronico, agudo) = Decidir(p, rollCronico: false, rollAgudo: true);

        Assert.Null(dx);   // el llamador sortea un motivo nuevo
        Assert.False(cronico);
        Assert.False(agudo);
    }

    [Fact]
    public void SinCita_RollsEnContra_NoHayControl()
    {
        var p = new SimulatedPatient
        {
            CronicasActivas = [Dx("hta", cronica: true)],
            UltimoDxAgudo = Dx("gripe"),
            FechaUltimoDxAgudo = Hoy.AddDays(-5)
        };

        var (dx, cronico, agudo) = Decidir(p, rollCronico: false, rollAgudo: false);

        Assert.Null(dx);
        Assert.False(cronico);
        Assert.False(agudo);
    }

    [Fact]
    public void ConCitaHoy_SinMotivoRegistrado_CaeALaLogicaProbabilistica()
    {
        // Citas creadas antes de esta feature (o sin dx primario): no rompen, siguen el camino anterior.
        var cronica = Dx("erc", cronica: true, categoria: "urologico");
        var p = new SimulatedPatient
        {
            ProximaCita = Hoy,
            MotivoProximaCita = null,
            CronicasActivas = [cronica]
        };

        var (dx, cronico, _) = Decidir(p, rollCronico: true);

        Assert.Same(cronica, dx);
        Assert.True(cronico);
    }
}
