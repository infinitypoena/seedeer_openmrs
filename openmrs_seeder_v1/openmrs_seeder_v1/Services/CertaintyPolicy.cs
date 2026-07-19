using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Certeza (<c>certainty</c>) de cada diagnóstico del encuentro — seam puro estático.
/// <para>
/// Antes era un 70 % CONFIRMED al azar, sin relación con si el cuadro tiene prueba. Ahora se condiciona
/// al CATÁLOGO (la decisión se toma al crear el encuentro, antes de que corran las órdenes, así que no
/// puede mirar el resultado real): un dx cuyo examen confirmatorio se procesa en la clínica sale
/// CONFIRMED (el resultado llega dentro de la misma visita); si el examen se refiere a un laboratorio
/// externo, PROVISIONAL (pendiente de confirmación — clínicamente exacto); en una visita de control el
/// cuadro ya se estudió en el episodio anterior → CONFIRMED. Sin confirmatorio en el catálogo, la
/// tirada histórica del 70 %.
/// </para>
/// </summary>
public static class CertaintyPolicy
{
    public const string Confirmed   = "CONFIRMED";
    public const string Provisional = "PROVISIONAL";

    /// <summary>Prob. histórica de CONFIRMED cuando el dx no tiene examen confirmatorio.</summary>
    public const double ProbConfirmadoSinExamen = 0.70;

    /// <param name="confirmatorios">Índice inverso dx → labs (<see cref="CatalogLoader.LabsConfirmatorios"/>).</param>
    /// <param name="esVisitaControl">La visita es un control de un cuadro ya conocido (cita/crónica/agudo).</param>
    public static string Certainty(
        DiagnosticoEntry dx,
        IReadOnlyDictionary<string, IReadOnlyList<LaboratorioEntry>> confirmatorios,
        bool esVisitaControl,
        Random rng)
    {
        if (esVisitaControl) return Confirmed;

        if (confirmatorios.TryGetValue(dx.CielUuid, out var labs) && labs.Count > 0)
            return labs.Any(l => l.SeRealizaEnClinica) ? Confirmed : Provisional;

        return rng.NextDouble() < ProbConfirmadoSinExamen ? Confirmed : Provisional;
    }
}
