namespace OpenmrsSeeder.Models.Catalogs;

public class LaboratorioEntry
{
    public string CielUuid { get; set; } = "";
    public string NombreEs { get; set; } = "";
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

    // ── Generación de resultado (v1: numeric | coded; panel/imagen/"" = sin resultado) ──
    /// <summary><c>numeric</c> | <c>coded</c> | <c>panel</c> | <c>imagen</c> (vacío = sin resultado).</summary>
    public string Datatype { get; set; } = "";
    /// <summary>Banda numérica normal (inclusive).</summary>
    public double ResMin { get; set; }
    public double ResMax { get; set; }
    /// <summary>Banda numérica anormal (cuando la enfermedad del paciente dispara el examen).</summary>
    public double ResMinAnormal { get; set; }
    public double ResMaxAnormal { get; set; }
    /// <summary>UUID de la respuesta "normal" para tests codificados (p.ej. Negativo).</summary>
    public string ResNormalUuid { get; set; } = "";
    /// <summary>UUID de la respuesta "anormal" para tests codificados (p.ej. Positivo).</summary>
    public string ResAnormalUuid { get; set; } = "";
    /// <summary>Categorías que hacen anormal el resultado (para numéricos, p.ej. diabetes|endocrino).</summary>
    public List<string> ResTrigger { get; set; } = [];
    /// <summary>UUIDs de diagnósticos específicos que hacen anormal el resultado (para codificados disease-specific, p.ej. dengue → NS1 Positivo).</summary>
    public List<string> ResTriggerDx { get; set; } = [];

    // ── Dónde se procesa y cuánto tarda ───────────────────────────────────────
    /// <summary>
    /// La clínica tiene capacidad para hacer este examen: la muestra se toma y se procesa aquí mismo y el
    /// resultado sale el mismo día. <c>false</c> = se refiere a un laboratorio externo (la clínica solo
    /// recibe el resultado días después). Columna ausente → <c>true</c> (comportamiento histórico).
    /// </summary>
    public bool SeRealizaEnClinica { get; set; } = true;
    /// <summary>Días hasta que el resultado está disponible (0 = mismo día). Banda inclusiva.</summary>
    public int DiasEntregaMin { get; set; }
    public int DiasEntregaMax { get; set; }
}
