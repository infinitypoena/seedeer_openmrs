namespace OpenmrsSeeder.Models.Catalogs;

public class DiagnosticoEntry
{
    public string CielUuid { get; set; } = "";
    public string NombreEs { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Severidad { get; set; } = "";
    public bool Aplica0_14 { get; set; }
    public bool Aplica15_29 { get; set; }
    public bool Aplica30_44 { get; set; }
    public bool Aplica45_64 { get; set; }
    public bool Aplica65mas { get; set; }
    public int PesoM { get; set; }
    public int PesoF { get; set; }
    public bool RequiereLab { get; set; }
    public bool RequiereRx { get; set; }
    public bool RequiereExamenClinico { get; set; }
}
