namespace OpenmrsSeeder.Models.Catalogs;

public class ExamenClinicoEntry
{
    public string CielUuid { get; set; } = "";
    public string NombreEs { get; set; } = "";
    /// <summary>numerico | categorico</summary>
    public string TipoResultado { get; set; } = "";
    public string Unidad { get; set; } = "";
    /// <summary>Banda del valor numérico (opcional; ambas en 0 = derivar de la unidad, retrocompatible).</summary>
    public double ResMin { get; set; }
    public double ResMax { get; set; }
    public bool AplicaRespiratorio { get; set; }
    public bool AplicaCardiovascular { get; set; }
    public bool AplicaDiabetes { get; set; }
    public bool AplicaDigestivo { get; set; }
    public bool AplicaOsteomuscular { get; set; }
    public bool AplicaUrologico { get; set; }
    public bool AplicaInfeccioso { get; set; }
    public bool AplicaEndocrino { get; set; }
    public bool AplicaNeurologico { get; set; }
    public bool AplicaDermatologico { get; set; }
    public bool AplicaSaludMental { get; set; }
    public bool AplicaGinecoobstetrico { get; set; }
    public bool AplicaTrauma { get; set; }
}
