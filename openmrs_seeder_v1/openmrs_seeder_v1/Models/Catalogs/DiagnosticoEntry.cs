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
    /// <summary>Estaciones (en minúscula) bajo las que la enfermedad es más frecuente. Vacío = sin efecto estacional.</summary>
    public HashSet<string> Clima { get; set; } = [];
    /// <summary>Si es una condición crónica → se agrega a la lista de problemas del paciente (POST /condition).</summary>
    public bool EsCronica { get; set; }
    /// <summary>Si es una enfermedad común/frecuente. El factor inicial de selección apunta mayormente a estas.</summary>
    public bool EsComun { get; set; }
}
