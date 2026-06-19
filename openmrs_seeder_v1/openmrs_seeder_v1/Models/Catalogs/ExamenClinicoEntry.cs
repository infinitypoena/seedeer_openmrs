namespace OpenmrsSeeder.Models.Catalogs;

public class ExamenClinicoEntry
{
    public string CielUuid { get; set; } = "";
    public string NombreEs { get; set; } = "";
    /// <summary>numerico | categorico</summary>
    public string TipoResultado { get; set; } = "";
    public string Unidad { get; set; } = "";
    public bool AplicaRespiratorio { get; set; }
    public bool AplicaCardiovascular { get; set; }
    public bool AplicaDiabetes { get; set; }
    public bool AplicaDigestivo { get; set; }
    public bool AplicaOsteomuscular { get; set; }
    public bool AplicaUrologico { get; set; }
    public bool AplicaInfeccioso { get; set; }
    public bool AplicaEndocrino { get; set; }
}
