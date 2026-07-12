namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>
/// Programa de atención de OpenMRS (catálogo <c>programas.csv</c>). La inscripción
/// (<c>POST /programenrollment</c>) se dispara cuando alguno de los diagnósticos del paciente
/// (por UUID) o alguna de sus categorías coincide con el disparo definido aquí.
/// </summary>
public class ProgramaEntry
{
    /// <summary>UUID del programa en ESTA instancia (verificar con <c>GET /program</c>).</summary>
    public string ProgramUuid { get; set; } = "";
    public string Nombre { get; set; } = "";
    /// <summary>UUIDs de diagnóstico (CIEL) que disparan la inscripción.</summary>
    public List<string> TriggerDx { get; set; } = [];
    /// <summary>Categorías que disparan la inscripción (disparo alterno; vacío = solo por dx).</summary>
    public List<string> TriggerCategoria { get; set; } = [];
    /// <summary>Estado inicial del workflow (opcional; vacío = enrollment sin estado).</summary>
    public string EstadoInicialUuid { get; set; } = "";
}
