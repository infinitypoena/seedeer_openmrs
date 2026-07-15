using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Qué hace la clínica cuando el cuadro se le sale de las manos. Es una **clínica de primer nivel**: no
/// opera un abdomen agudo ni maneja un infarto — lo detecta, lo estabiliza y lo <b>refiere al hospital</b>.
///
/// <para>La decisión es <b>determinista</b>, no una tirada de dados: una apendicitis no se refiere "con
/// probabilidad 0,8". O el cuadro es de los que la clínica no resuelve, o no lo es. Lo dice la columna
/// <c>ambito</c> del catálogo (<see cref="DiagnosticoEntry.EsReferencia"/>), curada a mano.</para>
///
/// <para>⚠️ <b><c>severidad=grave</c> NO significa referir.</b> Muchas graves las maneja el primer nivel:
/// VIH, tuberculosis (el DOTS es de primer nivel), pie diabético, retinopatía diabética o trastorno
/// bipolar — y ya tienen sus programas de atención. Referirlas sería un error clínico. La severidad solo
/// decide la <b>prioridad</b> del traslado, no si hay traslado.</para>
///
/// Seam estático puro (sin red, sin RNG) → testeable de forma determinista.
/// </summary>
public static class ReferenciaPolicy
{
    // Conceptos CIEL verificados contra esta instancia (clase Question + datatype Coded/Text, y los
    // valores están en la answer list declarada de su pregunta).
    public const string RemisionesSolicitadasUuid = "1272AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // Question/Coded
    public const string HospitalUuid              = "1589AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // answer de 1272
    public const string ReferidoAHospitalUuid     = "1788AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // Question/Coded
    public const string SiUuid                    = "1065AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // answer "Sí"
    public const string PrioridadReferenciaUuid   = "1885AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // Question/Coded
    public const string PrioridadEmergenciaUuid   = "1882AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // answer "Emergencia"
    public const string PrioridadUrgenteUuid      = "1883AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // answer "Urgente"
    public const string MotivoReferenciaUuid      = "164359AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // Question/Text

    /// <summary>¿Alguno de los diagnósticos de la visita obliga a mandar al paciente al hospital?</summary>
    public static bool DebeReferir(IEnumerable<DiagnosticoEntry> dxs) =>
        dxs.Any(d => d.EsReferencia);

    /// <summary>
    /// Prioridad del traslado: <b>Emergencia</b> si el cuadro es grave (sale ya, ambulancia),
    /// <b>Urgente</b> si no. Aquí sí manda la severidad — decide la prisa, no el traslado.
    /// </summary>
    public static string Prioridad(IEnumerable<DiagnosticoEntry> dxs) =>
        dxs.Any(d => d.EsReferencia && d.Severidad.Equals("grave", StringComparison.OrdinalIgnoreCase))
            ? PrioridadEmergenciaUuid
            : PrioridadUrgenteUuid;

    /// <summary>Texto del motivo que se escribe en la referencia, con los cuadros que la justifican.</summary>
    public static string Motivo(IEnumerable<DiagnosticoEntry> dxs)
    {
        var causas = dxs.Where(d => d.EsReferencia).Select(d => d.NombreEs).ToList();
        return causas.Count == 0
            ? ""
            : $"{string.Join(", ", causas)}. Cuadro fuera de la capacidad resolutiva del primer nivel: " +
              "se estabiliza y se refiere a hospital de segundo nivel.";
    }
}
