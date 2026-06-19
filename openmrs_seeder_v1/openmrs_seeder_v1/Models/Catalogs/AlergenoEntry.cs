namespace OpenmrsSeeder.Models.Catalogs;

public class AlergenoEntry
{
    public string ConceptUuid { get; set; } = "";
    public string NombreEs { get; set; } = "";
    /// <summary>DRUG | FOOD | ENVIRONMENT</summary>
    public string TipoAlergeno { get; set; } = "";
    /// <summary>leve | moderada | grave</summary>
    public string SeveridadTipica { get; set; } = "";
}
