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
}
