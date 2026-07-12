namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>Nombre de pila del catálogo de nombres, con su género (M | F).</summary>
public class NombreEntry
{
    public string Nombre { get; set; } = "";
    /// <summary>M | F</summary>
    public string Genero { get; set; } = "";
}
