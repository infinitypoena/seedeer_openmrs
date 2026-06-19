using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Models.Simulation;

public class SimulatedPatient
{
    public string Identifier { get; set; } = "";
    public string OpenMrsUuid { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    /// <summary>M | F</summary>
    public string Gender { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    /// <summary>0-14 | 15-29 | 30-44 | 45-64 | 65+</summary>
    public string AgeGroup { get; set; } = "";
    /// <summary>Categoría diagnóstica elegida del perfil epidemiológico</summary>
    public string Categoria { get; set; } = "";
    /// <summary>Diagnóstico elegido del catálogo — disponible tras EpidemiologySelector</summary>
    public DiagnosticoEntry? Diagnostico { get; set; }
    public string Address1 { get; set; } = "";
    public string City { get; set; } = "";
    public bool EsNuevo { get; set; } = true;
    /// <summary>UUID de la visita creada en OpenMRS</summary>
    public string VisitUuid { get; set; } = "";
    /// <summary>UUID del encuentro ADULTINITIAL (ConsultaSeeder) — usado por LabOrderSeeder y PrescriptionSeeder</summary>
    public string ConsultaEncounterUuid { get; set; } = "";
    /// <summary>Datetime de la visita (fecha simulada + hora realista del día)</summary>
    public DateTime VisitDatetime { get; set; }
}
