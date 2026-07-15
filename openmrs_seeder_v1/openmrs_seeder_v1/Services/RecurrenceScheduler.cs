using OpenmrsSeeder.Configuration;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Decide, de forma pura y testeable, la próxima fecha en que un paciente puede volver a consulta.
/// Impone un intervalo mínimo clínicamente plausible entre visitas (evita retornos día-a-día).
/// </summary>
public static class RecurrenceScheduler
{
    /// <summary>
    /// Devuelve la fecha más temprana en que <paramref name="ultimaVisita"/> puede repetirse: un
    /// número aleatorio de días dentro de la banda crónica (control mensual/trimestral) o aguda
    /// (seguimiento de 1–3 semanas) según <paramref name="esCronico"/>.
    /// </summary>
    public static DateOnly ProximaFechaElegible(
        DateOnly ultimaVisita, bool esCronico, Random rng, RecurrenceSettings s)
    {
        var min = esCronico ? s.MinDiasCronico : s.MinDiasAgudo;
        var max = esCronico ? s.MaxDiasCronico : s.MaxDiasAgudo;
        if (max < min) max = min;
        var dias = rng.Next(min, max + 1);
        return ultimaVisita.AddDays(dias);
    }

    /// <summary>
    /// Cuándo vuelve a la clínica el paciente que hoy se ha referido al hospital: es el <b>control
    /// post-alta</b>, así que tiene banda propia (15-30 d por defecto) — ni la aguda (7-21 d: aún estaría
    /// ingresado) ni la crónica (30-120 d: demasiado tarde para ver cómo salió).
    /// </summary>
    public static DateOnly ProximaFechaPostReferencia(DateOnly visita, Random rng, RecurrenceSettings s)
    {
        var min = s.MinDiasPostReferencia;
        var max = s.MaxDiasPostReferencia;
        if (max < min) max = min;
        return visita.AddDays(rng.Next(min, max + 1));
    }
}
