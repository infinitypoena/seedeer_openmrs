namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>Regla clínica investigada que clasifica un diagnóstico y, cuando corresponde, sus fármacos.</summary>
public class TratamientoEntry
{
    public string DiagnosticoUuid { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Accion { get; set; } = "";
    public List<string> MedicamentoUuids { get; set; } = [];
    public int MaxMedicamentos { get; set; } = 1;
    public string Fuente { get; set; } = "";
    public string VersionGuia { get; set; } = "";
}