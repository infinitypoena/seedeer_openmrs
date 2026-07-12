namespace OpenmrsSeeder.Models.Catalogs;

public class MedicamentoEntry
{
    public string DrugUuid { get; set; } = "";
    public string ConceptUuid { get; set; } = "";
    public string NombreGenerico { get; set; } = "";
    public string Strength { get; set; } = "";
    public string ViaUuid { get; set; } = "";
    /// <summary>Dosis por toma (p.ej. 1, 0.5, 5). 0 = sin especificar → 1 (comportamiento histórico).</summary>
    public double Dosis { get; set; }
    /// <summary>UUID de la unidad de dosis (tableta, mL, mg). Vacío = tableta por defecto.</summary>
    public string UnidadDosisUuid { get; set; } = "";
    /// <summary>UUID de la frecuencia (p.ej. cada 8 h, dos veces al día). Vacío = una vez al día por defecto.</summary>
    public string FrecuenciaUuid { get; set; } = "";
    /// <summary>Días de tratamiento. 0 = sin especificar → duración aleatoria histórica (7/14/30).</summary>
    public int DiasTratamiento { get; set; }
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
