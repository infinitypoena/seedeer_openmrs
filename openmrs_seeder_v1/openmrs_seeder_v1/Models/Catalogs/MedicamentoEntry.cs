namespace OpenmrsSeeder.Models.Catalogs;

public class MedicamentoEntry
{
    public string DrugUuid { get; set; } = "";
    public string ConceptUuid { get; set; } = "";
    public string NombreGenerico { get; set; } = "";
    public string Strength { get; set; } = "";
    public string ViaUuid { get; set; } = "";
    public bool AplicaRespiratorio { get; set; }
    public bool AplicaCardiovascular { get; set; }
    public bool AplicaDiabetes { get; set; }
    public bool AplicaDigestivo { get; set; }
    public bool AplicaOsteomuscular { get; set; }
    public bool AplicaUrologico { get; set; }
    public bool AplicaInfeccioso { get; set; }
    public bool AplicaEndocrino { get; set; }
}
