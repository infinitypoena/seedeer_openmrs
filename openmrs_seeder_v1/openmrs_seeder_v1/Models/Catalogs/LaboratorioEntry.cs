namespace OpenmrsSeeder.Models.Catalogs;

public class LaboratorioEntry
{
    public string CielUuid { get; set; } = "";
    public string NombreEs { get; set; } = "";
    public string Clase { get; set; } = "";
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
