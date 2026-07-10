namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>
/// Fila de <c>direcciones.csv</c>: una zona residencial (colonia/barrio/cantón) de un municipio.
/// El peso concentra a los pacientes cerca de la clínica (área metropolitana) y deja una cola de
/// municipios lejanos, como la captación real de una clínica de consulta externa.
/// </summary>
public class DireccionEntry
{
    public string Departamento { get; set; } = "";
    public string Municipio { get; set; } = "";
    /// <summary>Colonia, barrio o cantón (texto tal cual va en address1, p.ej. "Colonia Zacamil").</summary>
    public string Zona { get; set; } = "";
    public int Peso { get; set; } = 1;
}
