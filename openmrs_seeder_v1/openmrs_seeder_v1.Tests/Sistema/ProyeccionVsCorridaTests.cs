using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Sistema;

/// <summary>
/// La proyección determinista (etapa 2/5) y la corrida tienen que <b>hablar el mismo idioma</b> — misma
/// magnitud (media móvil de 30 días abiertos) y mismo orden de valores. ⚠️ Nunca igualdad numérica: la
/// proyección es una estimación de cohortes y la corrida cuenta el pool real. Lo que se rompió una vez
/// fue exactamente esto: se calibraba sobre un modelo que no era el que corría (estimaba 8,2
/// visitas/paciente-año contra 1,45 reales), la bisección elegía una q 3× mayor y la clínica pasaba 33
/// meses contra el techo.
/// </summary>
public class ProyeccionVsCorridaTests
{
    [Fact]
    public void ElPicoProyectado_YLaMesetaDeLaCorrida_CoincidenEnBanda()
    {
        var sim  = Escenarios.SimObjetivo();
        var plan = new DailyScheduleGenerator(sim).Generate();
        var q    = BassGrowthModel.CoeficienteImitacion(plan, sim);

        var picoProyectado = BassGrowthModel.MediasProyectadas(plan, sim, q).Pico;
        var mesetaReal     = MiniClinica.Correr(sim).Stats.MediaDiariaMaxima;

        // ±30 %: banda ancha a propósito (estimación vs. corrida), pero suficiente para cazar una
        // calibración desquiciada (la histórica proyectaba 45+ contra una realidad recortada).
        var desvio = Math.Abs(picoProyectado - mesetaReal) / picoProyectado;
        Assert.True(desvio <= 0.30,
            $"Proyección {picoProyectado:0.0}/día contra corrida {mesetaReal:0.0}/día ({desvio:P0} de desvío)");
    }

    [Theory]
    [InlineData(25)]
    [InlineData(35)]
    public void LaCalibracion_AterrizaEnElObjetivoPedido(int objetivo)
    {
        // El contrato de CalibrarImitacion: el usuario dice el destino y q se deriva para llegar A ESE
        // destino (medido como el PICO de la media móvil, no el último día).
        var sim  = Escenarios.SimObjetivo(objetivo: objetivo, max: Math.Max(45, (int)(objetivo * 1.5)));
        var plan = new DailyScheduleGenerator(sim).Generate();
        var q    = BassGrowthModel.CalibrarImitacion(plan, sim);

        var pico = BassGrowthModel.MediasProyectadas(plan, sim, q).Pico;

        Assert.InRange(pico, objetivo * 0.8, objetivo * 1.2);
    }

    [Fact]
    public void UnObjetivoQueElPanelYaCubre_NoNecesitaBocaABoca()
    {
        // El suelo: 6 altas/día ya producen ~1/(1−k) visitas cada una. Pedir una meseta por debajo de ese
        // suelo debe derivar q = 0 (la clínica no necesita crecer), no un boca a boca negativo/absurdo.
        var sim  = Escenarios.SimObjetivo(arranque: 10, objetivo: 12);
        var plan = new DailyScheduleGenerator(sim).Generate();

        Assert.Equal(0, BassGrowthModel.CalibrarImitacion(plan, sim));
    }

    [Fact]
    public void LaCurvaDocumentada_ArrancaBajaYMaduraElMix()
    {
        // El ancla de CLAUDE.md (6,2/día en el primer mes → ~25 al cierre, mix 4 % → 60 %): la corrida
        // del harness tiene que reproducir la FORMA — arranque en la banda del arranque, meseta en la del
        // objetivo y el mix del último año completo en la banda de régimen.
        var r     = MiniClinica.Correr(Escenarios.SimObjetivo());
        var anios = r.Stats.PorAnio();
        var mix2025 = anios.Single(a => a.Anio == 2025) is var u && u.Total > 0
            ? (double)u.Recurrentes / u.Total
            : 0;

        Assert.InRange(r.Stats.MediaDiariaInicial, 4, 12);      // arranca donde se le dijo
        Assert.InRange(r.Stats.MediaDiariaMaxima, 20, 30);      // llega a donde se le pidió
        Assert.InRange(mix2025, 0.40, 0.85);                    // y el panel sostiene la consulta
    }
}
