using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;
using openmrs_seeder_v1.Tests.TestSupport;
using Xunit;

namespace openmrs_seeder_v1.Tests.Sistema;

/// <summary>
/// <b>El test del sistema, en negativo</b>: las formas conocidas de romper la clínica tienen que poner
/// las leyes en rojo. La portada es la corrida rota real de 3,5 años (la que pasó 380 tests en verde y
/// costó tres meses descubrir); el segundo es su causa reintroducida a propósito (el cupo de
/// recurrentes) corriendo en la <see cref="MiniClinica"/> — si alguien vuelve a meter un cupo, este test
/// es el que lo delata.
/// </summary>
public class SistemaRotoTests
{
    [Fact]
    public void LaCorridaRotaDeTresAnosYMedio_RompeLasLeyesQueDeberia()
    {
        // Números REALES de la corrida 2023-01-01 → 2026-06-30 con el cupo fijo del 30 %:
        //   · 31.610 pacientes captados, de los que 24.989 (79 %) vinieron una sola vez
        //   · 11.808 crónicos, de los que solo 4.122 (34,9 %) volvieron alguna vez
        //   · 14.115 citas Completed contra 14.025 Missed (49,8 % de no-show)
        //   · mix de recurrentes clavado en el 31 %, el primer mes y el último
        //   · 33 de los 42 meses con la media diaria pegada al techo de 45/día
        var sim = Escenarios.SimObjetivo();
        sim.EndDate = new DateTime(2026, 6, 30);

        var pool = new List<SimulatedPatient>();
        for (var i = 0; i < 4122; i++)  pool.Add(Factorias.P(visitas: 3, cronico: true));   // volvieron
        for (var i = 0; i < 7686; i++)  pool.Add(Factorias.P(visitas: 1, cronico: true));   // ABANDONADOS
        for (var i = 0; i < 2499; i++)  pool.Add(Factorias.P(visitas: 2));
        for (var i = 0; i < 17303; i++) pool.Add(Factorias.P(visitas: 1));                  // el 79 %

        var stats = Estadisticas.Sembrar(
            porAnio: [(2023, 6000, 2700), (2026, 5000, 2250)],   // 31 % de recurrentes, plano
            citasCumplidas: 14115,
            citasPerdidas: 14025,
            diasConAtencion: 1094,
            diasEnElTecho: 860,                                  // 79 % de los días contra el techo
            atendidosPorDia: 45);                                // clavada en PacientesPorDiaMax

        var leyes = Invariantes.Evaluar(stats, pool, sim);
        var rotas = Invariantes.Rotas(leyes).Select(l => l.Codigo).ToList();

        // El crónico no vuelve, la agenda se tira a la basura, el panel no madura, la curva es una pared
        // y el paciente es un ticket. Cinco leyes rojas — exactamente lo que había que cazar.
        Assert.Contains("L1", rotas);
        Assert.Contains("L2", rotas);
        Assert.Contains("L4", rotas);
        Assert.Contains("L5", rotas);
        Assert.Contains("L7", rotas);

        Assert.Contains("34,9", leyes.Single(l => l.Codigo == "L1").Medido.Replace('.', ','));
    }

    [Fact]
    public void ReintroducirElCupoDeRecurrentes_PoneElSistemaEnRojo()
    {
        // El sabotaje: la MISMA configuración sana de SistemaSanoTests, pero con el reparto del día del
        // bug histórico (total = altas / (1 − 30 %) y los retornos recortados al residuo). Con la suite
        // vieja esto pasaba en verde; ahora tiene que encender el tablero.
        var r = MiniClinica.Correr(
            Escenarios.SimObjetivo(),
            new OpcionesMiniClinica { CupoRecurrentes = 0.30 });

        var rotas = r.Rotas.Select(l => l.Codigo).ToList();

        Assert.Contains("L1", rotas);   // el crónico no vuelve a su control
        Assert.Contains("L2", rotas);   // la agenda se tira a la basura
        Assert.Contains("L3", rotas);   // el tripwire: retornos desplazados por el cupo
        Assert.Contains("L7", rotas);   // el paciente vuelve a ser un ticket
        Assert.Contains("L8", rotas);   // y la clínica ya no llega a donde se le pidió

        // Las magnitudes del desastre son las del bug real: ~1,4 visitas/paciente (histórico: 1,45)
        // y más de la mitad de la agenda perdida (histórico: 49,8 %).
        Assert.True(r.Stats.VisitasPorPaciente < 2.0);
        Assert.True(r.Stats.FraccionCitasPerdidas > 0.40,
            $"Se perdió el {r.Stats.FraccionCitasPerdidas:P1} de la agenda");
    }

    [Fact]
    public void UnaClinicaQueAtiendeMal_SeQuedaSinPanel()
    {
        // El churn como modo de fallo: notas hundidas → todo el mundo insatisfecho → no acuden a sus
        // citas (0,20) ni vuelven por su cuenta. La continuidad longitudinal muere aunque el reparto
        // del día esté perfecto.
        var sim = Escenarios.SimObjetivo();
        sim.Satisfaccion.MediaBase = 1.5;

        var r = MiniClinica.Correr(sim);
        var rotas = r.Rotas.Select(l => l.Codigo).ToList();

        Assert.Contains("L1", rotas);
        Assert.Contains("L7", rotas);
    }
}
