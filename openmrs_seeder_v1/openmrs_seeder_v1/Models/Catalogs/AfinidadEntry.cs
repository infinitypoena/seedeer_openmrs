namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>
/// Cluster de comorbilidad: una categoría y las categorías clínicamente afines a ella.
/// Usado por EpidemiologySelector para favorecer comorbilidades coherentes.
/// </summary>
public class AfinidadEntry
{
    public string Categoria { get; set; } = "";
    /// <summary>Categorías afines (en el CSV van separadas por '|').</summary>
    public List<string> Afines { get; set; } = [];
}
