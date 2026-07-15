using OpenmrsSeeder.Configuration;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Contexto de la visita que se está calificando. Todo sale de lo que el simulador ya sabe: si atendió
/// el médico de cabecera del paciente (continuidad asistencial), si acudió a una cita agendada
/// (le esperaban) y si el cuadro era grave.
/// </summary>
/// <param name="AtendidoPorCabecera">Le atendió SU médico de siempre.</param>
/// <param name="AcudioACita">Vino a una cita de control agendada, no de improviso.</param>
/// <param name="CuadroGrave">Alguno de sus diagnósticos es de severidad grave.</param>
/// <param name="PacientesDelDia">Pacientes atendidos ese día (para medir la saturación de la clínica).</param>
public readonly record struct ContextoVisita(
    bool AtendidoPorCabecera,
    bool AcudioACita,
    bool CuadroGrave,
    int PacientesDelDia);

/// <summary>
/// La nota (1-5) que el paciente le pone a la visita. Seam estático puro (RNG inyectado), sin red:
/// nada de esto se escribe en OpenMRS, es estado de simulación que alimenta al modelo de crecimiento.
///
/// La pieza importante es la <b>penalización por saturación</b>: es el lazo de realimentación NEGATIVA
/// del sistema. Crecer llena la consulta; la consulta llena atiende peor; peor atención deja menos
/// pacientes satisfechos; y sin satisfechos no hay boca a boca. Sin este término el crecimiento no
/// tendría más freno que agotar la población del área.
/// </summary>
public static class SatisfaccionPolicy
{
    public const int NotaMinima = 1;
    public const int NotaMaxima = 5;

    /// <summary>
    /// Cuánto se ha pasado la clínica de su capacidad cómoda, en tantos por uno: 0 = va holgada,
    /// 1,0 = está atendiendo el doble de lo que puede sin que se note la espera.
    /// </summary>
    public static double Saturacion(int pacientesDelDia, int capacidadComoda)
    {
        if (capacidadComoda <= 0) return 0;
        return Math.Max(0, (pacientesDelDia - (double)capacidadComoda) / capacidadComoda);
    }

    /// <summary>
    /// Nota entera 1-5 de la visita: media base, más los bonus del contexto, menos las penalizaciones,
    /// más ruido normal — y redondeada a la escala.
    /// </summary>
    public static int Calificar(ContextoVisita ctx, SatisfaccionSettings s, Random rng)
    {
        var nota = s.MediaBase;

        if (ctx.AtendidoPorCabecera) nota += s.BonusMedicoCabecera;
        if (ctx.AcudioACita)         nota += s.BonusCitaCumplida;
        if (ctx.CuadroGrave)         nota -= s.PenalizacionCuadroGrave;

        nota -= s.PenalizacionSaturacion * Saturacion(ctx.PacientesDelDia, s.CapacidadComodaPorDia);
        nota += RuidoNormal(rng) * s.Desviacion;

        return Math.Clamp((int)Math.Round(nota, MidpointRounding.AwayFromZero), NotaMinima, NotaMaxima);
    }

    /// <summary>
    /// ¿El paciente quedó descontento? Promedio ≤ umbral. Sin ninguna calificación NO está insatisfecho
    /// (un paciente nuevo aún no ha opinado).
    /// </summary>
    public static bool EsInsatisfecho(IReadOnlyList<int> calificaciones, double umbral) =>
        calificaciones.Count > 0 && calificaciones.Average() <= umbral;

    /// <summary>
    /// Fracción de pacientes que quedarían satisfechos en una visita neutra con esa
    /// <paramref name="saturacion"/>, según el propio modelo de nota:
    /// P(round(N(MediaBase − Penalización×saturación, Desviacion)) &gt; umbral).
    ///
    /// <para>Alimenta la proyección determinista (<see cref="BassGrowthModel.Proyectar"/>) y, a través de
    /// ella, la calibración del boca a boca. ⚠️ Tiene que <b>incluir el efecto de la saturación</b>: si no,
    /// la proyección sobreestima cuántos pacientes quedan contentos, la calibración apunta demasiado alto
    /// y la corrida real aterriza por debajo del objetivo.</para>
    /// </summary>
    public static double FraccionSatisfechaTeorica(SatisfaccionSettings s, double saturacion = 0)
    {
        var media = s.MediaBase - s.PenalizacionSaturacion * Math.Max(0, saturacion);
        // Redondear a entero y exigir > umbral equivale a superar el punto medio de la nota siguiente.
        var corte = Math.Floor(s.UmbralSatisfaccion) + 0.5;
        if (s.Desviacion <= 0) return media > s.UmbralSatisfaccion ? 1.0 : 0.0;
        return 1.0 - Phi((corte - media) / s.Desviacion);
    }

    /// <summary>Función de distribución acumulada de la normal estándar (aproximación de Abramowitz-Stegun 7.1.26).</summary>
    private static double Phi(double x)
    {
        var signo = x < 0 ? -1 : 1;
        var z = Math.Abs(x) / Math.Sqrt(2.0);
        var t = 1.0 / (1.0 + 0.3275911 * z);
        var y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t
                       + 0.254829592) * t * Math.Exp(-z * z);
        return 0.5 * (1.0 + signo * y);
    }

    /// <summary>Normal estándar por Box-Muller (mismo método que DailyScheduleGenerator).</summary>
    private static double RuidoNormal(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
