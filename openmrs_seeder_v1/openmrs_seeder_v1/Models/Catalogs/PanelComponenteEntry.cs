namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>
/// Fila de <c>paneles.csv</c>: un componente de un panel de laboratorio (p.ej. la hemoglobina del
/// hemograma). El resultado del panel se registra como obs-group: obs padre (concepto del panel,
/// ligada a la orden) + una obs hija por componente. Bandas normal/anormal con disparo por
/// categoría, mismo patrón que <see cref="LaboratorioEntry"/>.
/// </summary>
public class PanelComponenteEntry
{
    /// <summary>UUID del concepto del panel (debe coincidir con una fila datatype=panel de laboratorios.csv).</summary>
    public string PanelUuid { get; set; } = "";
    public string ComponenteUuid { get; set; } = "";
    public string Nombre { get; set; } = "";
    public double ResMin { get; set; }
    public double ResMax { get; set; }
    public double ResMinAnormal { get; set; }
    public double ResMaxAnormal { get; set; }
    /// <summary>Categorías que disparan la banda anormal de ESTE componente (separadas por '|').</summary>
    public List<string> ResTrigger { get; set; } = [];
}
