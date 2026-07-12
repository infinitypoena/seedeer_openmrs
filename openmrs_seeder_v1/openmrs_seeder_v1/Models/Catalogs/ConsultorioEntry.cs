namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>Un consultorio del catálogo: su ubicación (Visit Location) y el médico que lo atiende.</summary>
public class ConsultorioEntry
{
    public string LocationUuid { get; set; } = "";
    /// <summary>Identificador único del proveedor/médico (idempotencia). Ej: "SIM-MED-C1".</summary>
    public string MedicoIdentifier { get; set; } = "";
    /// <summary>Nombre del médico a crear si no existe. Ej: "Carlos Méndez".</summary>
    public string MedicoNombre { get; set; } = "";
    /// <summary>Género del médico (M | F) para crear la persona. Default M si vacío.</summary>
    public string MedicoGenero { get; set; } = "M";
}
