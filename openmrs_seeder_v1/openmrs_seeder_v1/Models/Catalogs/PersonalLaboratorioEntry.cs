namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>
/// Personal del laboratorio (catálogo <c>personal_laboratorio.csv</c>). Los <c>tecnico</c> toman la
/// muestra y registran el resultado; el <c>responsable</c> lo valida (es quien cierra la orden en
/// <c>COMPLETED</c>). Son datos de referencia: identificadores <c>SIM-LAB-*</c>, reutilizados entre
/// corridas y no anulados por el subcomando <c>clear</c> (igual que los médicos <c>SIM-MED-*</c>).
/// </summary>
public class PersonalLaboratorioEntry
{
    public string Identifier { get; set; } = "";
    public string Nombre { get; set; } = "";
    /// <summary>M | F</summary>
    public string Genero { get; set; } = "M";
    /// <summary><c>tecnico</c> | <c>responsable</c></summary>
    public string Rol { get; set; } = "";

    public bool EsResponsable => Rol.Equals("responsable", StringComparison.OrdinalIgnoreCase);
}
