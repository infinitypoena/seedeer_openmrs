using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Simulation;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Qué pacientes del pool vuelven HOY a la consulta. Seam puro y testeable (RNG inyectado).
///
/// <para>⚠️ <b>Aquí NO hay cupo.</b> Lo que devuelve esta función es la <b>demanda real del panel</b>: los
/// pacientes que hoy tocaban. El volumen del día es una <i>consecuencia</i> de ella (nuevos + retornos),
/// no al revés.</para>
///
/// <para><b>El bug que esto corrige.</b> Antes el volumen del día salía de
/// <c>total = nuevos / (1 − PorcentajeRecurrentes)</c> y los recurrentes eran el residuo: un cupo fijo del
/// 30 %. Como la clínica agenda control en ~2 de cada 3 visitas (FollowUp crónico 0,90 · grave 0,80 ·
/// resto 0,30), llegaban ~20 citas al día a competir por 14 huecos. El sobrante vencía sin que nadie lo
/// atendiera. Medido en la corrida de 3,5 años: <b>14.025 citas Missed contra 14.115 Completed</b> — la
/// mitad de la agenda a la basura —, el <b>65 % de los crónicos no volvió jamás a un control</b> y el
/// relleno aleatorio del pool (la rama 2 de aquí abajo) <b>no llegó a ejecutarse nunca</b>: las citas se
/// comían el cupo entero todos los días.</para>
///
/// Dos vías de retorno, y ninguna compite con la otra:
/// <list type="number">
/// <item><b>La cita de control</b> (±tolerancia): el paciente al que se citó. Acude con su probabilidad
/// de asistencia; si no acude es un no-show de verdad (su cita acabará en <c>Missed</c>), no una víctima
/// del aforo.</item>
/// <item><b>El retorno espontáneo</b>: el paciente que sigue siendo de la clínica y ya cumplió su
/// intervalo mínimo vuelve <b>por su cuenta</b>, con una tasa diaria — una dolencia nueva, meses después.
/// Es "coger a cualquiera de la lista al azar", pero como <b>tasa por paciente</b>: así el número de
/// retornos <b>crece con el panel</b> en vez de quedarse clavado en un porcentaje.</item>
/// </list>
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
    /// ¿El paciente sigue siendo clientela de la clínica? (visitó dentro de la ventana de actividad).
    /// El que lleva más de un año sin aparecer ya no vuelve solo: habría que volver a captarlo.
    /// </summary>
    public static bool EstaActivo(SimulatedPatient p, DateOnly fecha, int ventanaActividadDias) =>
        p.UltimaVisita is { } u && fecha.DayNumber - u.DayNumber <= ventanaActividadDias;

    /// <summary>
    /// Probabilidad diaria de que un paciente activo y elegible vuelva por su cuenta, derivada del mando
    /// interpretable <c>VisitasEspontaneasPorPacienteAno</c> (proceso de Poisson: <c>1 − e^(−r/365)</c>).
    /// Nadie sabe apuntar una probabilidad diaria de 0,0014; todo el mundo sabe decir "un paciente pasa
    /// por aquí por su cuenta como una vez al año".
    /// </summary>
    public static double ProbRetornoEspontaneoDiaria(RecurrenceSettings re) =>
        re.VisitasEspontaneasPorPacienteAno <= 0
            ? 0
            : 1 - Math.Exp(-re.VisitasEspontaneasPorPacienteAno / 365.0);

    /// <summary>
    /// Pacientes del pool que vuelven hoy. <paramref name="elegibles"/> ya viene filtrado por el
    /// orquestador (no atendidos hoy y que cumplieron su intervalo mínimo entre visitas).
    ///
    /// <para>El paciente <see cref="SimulatedPatient.Insatisfecho"/> se aleja: acude a su cita con
    /// <paramref name="asistenciaProbInsatisfecho"/> (mucho más baja) y <b>no vuelve nunca por su
    /// cuenta</b>. Es el churn. Sin calificaciones nadie está insatisfecho y esto no tiene efecto.</para>
    /// </summary>
    public static List<SimulatedPatient> Seleccionar(
        IReadOnlyList<SimulatedPatient> elegibles,
        DateOnly fecha,
        int toleranciaDias,
        double asistenciaProb,
        double asistenciaProbInsatisfecho,
        double probRetornoEspontaneo,
        int ventanaActividadDias,
        Random rng)
    {
        var seleccion = new List<SimulatedPatient>();

        foreach (var p in elegibles)
        {
            // 1) Le citaron para hoy. Acude o no acude — pero el aforo no tiene voz aquí.
            if (TieneCitaHoy(p, fecha, toleranciaDias))
            {
                var prob = p.Insatisfecho ? asistenciaProbInsatisfecho : asistenciaProb;
                if (rng.NextDouble() < prob) seleccion.Add(p);
                // El no-show NO se cuela luego por la vía espontánea: su cita se resolverá como Missed.
                continue;
            }

            // 2) Vuelve por su cuenta: sigue siendo paciente de la clínica y le pasa algo nuevo.
            if (p.Insatisfecho) continue;
            if (probRetornoEspontaneo <= 0) continue;
            if (!EstaActivo(p, fecha, ventanaActividadDias)) continue;
            if (rng.NextDouble() < probRetornoEspontaneo) seleccion.Add(p);
        }

        return seleccion;
    }
}
