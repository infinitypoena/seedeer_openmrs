using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Elige, de forma pura y testeable, qué pacientes recurrentes se atienden hoy. La agenda gobierna el
/// retorno: primero entran los pacientes con una cita de control para hoy (±tolerancia), cada uno con
/// probabilidad de asistencia (el resto son no-shows que se resolverán como Missed); el cupo que quede
/// se rellena sorteando entre el resto de elegibles (comportamiento histórico). Así un crónico vuelve
/// en la fecha que le tocaba, en lugar de ser elegido al azar sin relación con su cita.
/// </summary>
public static class RecurrentSelector
{
    /// <summary>
    /// ¿El paciente viene HOY a su cita de control? (cita agendada dentro de ±tolerancia de la fecha).
    /// Es la misma noción que usa el orquestador para atenderlo con el médico y el motivo de esa cita.
    /// </summary>
    public static bool TieneCitaHoy(SimulatedPatient p, DateOnly fecha, int toleranciaDias) =>
        p.ProximaCita is { } c && Math.Abs(c.DayNumber - fecha.DayNumber) <= toleranciaDias;

    /// <summary>
    /// <paramref name="elegibles"/> ya viene filtrado (no atendidos hoy y que cumplieron su intervalo
    /// mínimo). Devuelve hasta <paramref name="cupo"/> pacientes, sin repetir, priorizando los que
    /// tienen <see cref="SimulatedPatient.ProximaCita"/> dentro de ±<paramref name="toleranciaDias"/>.
    /// </summary>
    public static List<SimulatedPatient> Seleccionar(
        IReadOnlyList<SimulatedPatient> elegibles, DateOnly fecha, int cupo,
        int toleranciaDias, double asistenciaProb, Random rng)
    {
        if (cupo <= 0 || elegibles.Count == 0) return [];

        var seleccion = new List<SimulatedPatient>(Math.Min(cupo, elegibles.Count));
        var usados = new HashSet<string>();

        // 1) Citas de control para hoy (±tolerancia): asisten con probabilidad asistenciaProb.
        //    Las citas más antiguas primero (llevan más tiempo esperando su control).
        var conCita = elegibles
            .Where(p => TieneCitaHoy(p, fecha, toleranciaDias))
            .OrderBy(p => p.ProximaCita!.Value.DayNumber)
            .ToList();
        var conCitaUuids = conCita.Select(p => p.OpenMrsUuid).ToHashSet();
        foreach (var p in conCita)
        {
            if (seleccion.Count >= cupo) break;
            if (rng.NextDouble() < asistenciaProb && usados.Add(p.OpenMrsUuid))
                seleccion.Add(p);
        }

        // 2) Relleno del cupo restante: sorteo entre el resto de elegibles SIN cita para hoy. Un paciente
        //    con cita que no asistió (no-show) no se cuela por azar: su cita queda pendiente → Missed.
        if (seleccion.Count < cupo)
        {
            foreach (var p in elegibles.Where(p => !conCitaUuids.Contains(p.OpenMrsUuid)).OrderBy(_ => rng.Next()))
            {
                if (seleccion.Count >= cupo) break;
                if (usados.Add(p.OpenMrsUuid)) seleccion.Add(p);
            }
        }

        return seleccion;
    }
}
