using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Sistema;

/// <summary>
/// <b>El test del sistema, en positivo</b>: la configuración canónica (arranca en 6 altas/día, apunta a
/// 25 visitas/día, 3 años) corrida entera en la <see cref="MiniClinica"/> tiene que dejar una clínica que
/// cumpla las 8 leyes. Esto es lo que faltaba cuando Bass entró: había 380 tests de piezas y ninguno que
/// corriera el sistema y lo juzgara como sistema.
/// </summary>
public class SistemaSanoTests
{
    // Una sola corrida compartida: es determinista (semilla fija) y así la clase entera cuesta ~1 s.
    private static readonly ResultadoMiniClinica Sano = MiniClinica.Correr(Escenarios.SimObjetivo());

    [Fact]
    public void TresAnosSanos_NingunaLeySeRompe()
    {
        Assert.Empty(Sano.Rotas);
    }

    [Fact]
    public void ElCronicoVuelveASuControl()
    {
        // L1 con margen: no "pasa por poco", pasa como pasa una clínica de verdad (~3/4 de los crónicos).
        var l1 = Sano.Ley("L1");
        Assert.True(l1.Aplica);
        Assert.True(l1.Cumple);
        var cronicos = Sano.Pool.Where(p => p.CronicasActivas.Count > 0).ToList();
        var volvieron = cronicos.Count(p => p.Visitas >= 2);
        Assert.True(volvieron / (double)cronicos.Count >= 0.70,
            $"Solo volvió el {100.0 * volvieron / cronicos.Count:0.0} % de los crónicos");
    }

    [Fact]
    public void ElPanelMadura_ElMixDeRecurrentesSubeAnoAAno()
    {
        var anios = Sano.Stats.PorAnio();
        double Mix((int Anio, int Total, int Nuevos, int Recurrentes) a) =>
            a.Total == 0 ? 0 : (double)a.Recurrentes / a.Total;

        // No solo primer año contra último (eso ya lo juzga L4): también el 2º supera al 1º — la
        // maduración es una rampa, no un salto en el borde de la ventana.
        Assert.True(Sano.Ley("L4").Cumple);
        Assert.True(Mix(anios[1]) > Mix(anios[0]),
            $"El mix no sube en el 2º año: {Mix(anios[0]):P1} → {Mix(anios[1]):P1}");
    }

    [Fact]
    public void LaAgendaSeHonra_YElAforoJamasDesplazaUnaCita()
    {
        Assert.True(Sano.Ley("L2").Cumple, Sano.Ley("L2").Medido);
        Assert.Equal(0, Sano.Stats.RetornosDesplazadosPorAforo);
        // El freno sano existe y actúa: hay altas sin captar (la consulta llena deja de captar)...
        Assert.True(Sano.Stats.NuevosRechazadosPorAforo > 0);
        // ...pero el techo DURO no gobierna la clínica (L5).
        Assert.Equal(0, Sano.Stats.DiasEnElTecho);
    }

    [Fact]
    public void LaClinicaAterrizaEnSuObjetivo()
    {
        // La meseta real (media móvil de un mes) queda en la banda del objetivo, y el arranque en la suya:
        // la curva documentada es 6/día el primer mes → ~25 al cierre.
        Assert.InRange(Sano.Stats.MediaDiariaInicial, 4, 12);
        Assert.InRange(Sano.Stats.MediaDiariaMaxima, 20, 30);
        Assert.True(Sano.Ley("L8").Cumple, Sano.Ley("L8").Medido);
    }

    [Fact]
    public void SinCitasReales_L2NoAplica_YNadaSeRompe()
    {
        // Con la feature de Appointments apagada no se postea ninguna cita → no hay citas que juzgar.
        // La ley debe decir "no procede", no aprobar en falso ni suspender en falso. El retorno del
        // paciente sigue funcionando (ProximaCita es estado interno del pool, no la cita de OpenMRS).
        var r = MiniClinica.Correr(Escenarios.SimObjetivo(), new OpcionesMiniClinica { CitasActivas = false });

        Assert.False(r.Ley("L2").Aplica);
        Assert.Empty(r.Rotas);
        Assert.True(r.Ley("L1").Cumple, r.Ley("L1").Medido);
    }

    [Fact]
    public void ConLaSatisfaccionApagada_ElBocaABocaArrancaIgual()
    {
        // La regresión del S(d): se medía con Calificaciones.Count, que con la satisfacción apagada es 0
        // para todo el mundo — y el boca a boca no arrancaba nunca. S debe salir de p.Visitas.
        var sim = Escenarios.SimObjetivo();
        sim.Satisfaccion.Enabled = false;

        var r = MiniClinica.Correr(sim);

        Assert.True(r.Ley("L8").Cumple,
            $"Con la satisfacción apagada la clínica no creció: {r.Ley("L8").Medido}");
        Assert.Empty(r.Rotas);
    }

    [Fact]
    public void LaMismaSemilla_DaLaMismaClinica()
    {
        var otra = MiniClinica.Correr(Escenarios.SimObjetivo());

        Assert.Equal(Sano.Stats.TotalVisitas, otra.Stats.TotalVisitas);
        Assert.Equal(Sano.Stats.PacientesUnicos, otra.Stats.PacientesUnicos);
        Assert.Equal(Sano.Stats.CitasCompletadas, otra.Stats.CitasCompletadas);
        Assert.Equal(Sano.Stats.CitasPerdidas, otra.Stats.CitasPerdidas);
    }
}
