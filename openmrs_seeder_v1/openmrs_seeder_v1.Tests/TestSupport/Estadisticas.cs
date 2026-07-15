using OpenmrsSeeder.Models.Simulation;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests.TestSupport;

/// <summary>
/// Siembra un <see cref="RunStats"/> a mano, para los tests de leyes que construyen el estado agregado
/// directamente (sin correr la <see cref="MiniClinica"/>): visitas repartidas por año, citas resueltas y
/// el comportamiento del aforo.
/// </summary>
public static class Estadisticas
{
    public static RunStats Sembrar(
        (int Anio, int Nuevos, int Recurrentes)[] porAnio,
        int citasCumplidas = 0,
        int citasPerdidas = 0,
        int diasConAtencion = 300,
        int diasEnElTecho = 0,
        int retornosDesplazados = 0,
        int atendidosPorDia = 20)
    {
        var stats = new RunStats();

        foreach (var (anio, nuevos, recurrentes) in porAnio)
        {
            var fecha = new DateOnly(anio, 6, 1);
            for (var i = 0; i < nuevos; i++)
                stats.RegistrarVisita(
                    new SimulatedPatient { OpenMrsUuid = $"p-{anio}-{i}", EsNuevo = true }, fecha);
            // Los recurrentes son los MISMOS pacientes dados de alta (eso es lo que los hace recurrentes):
            // así visitas/paciente sale de verdad y la ley L7 mide algo.
            for (var i = 0; i < recurrentes; i++)
                stats.RegistrarVisita(
                    new SimulatedPatient
                    {
                        OpenMrsUuid = $"p-{anio}-{(nuevos > 0 ? i % nuevos : i)}",
                        EsNuevo     = false,
                    },
                    fecha);
        }

        for (var i = 0; i < citasCumplidas; i++) stats.RegistrarCitaResuelta(cumplida: true);
        for (var i = 0; i < citasPerdidas; i++)  stats.RegistrarCitaResuelta(cumplida: false);

        for (var d = 0; d < diasConAtencion; d++)
            stats.RegistrarDiaSimulado(
                atendidos:           atendidosPorDia,
                nuevosRechazados:    0,
                retornosDesplazados: d == 0 ? retornosDesplazados : 0,
                topoElTecho:         d < diasEnElTecho);

        return stats;
    }
}
