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
    /// <summary>Diagnósticos adicionales (comorbilidad) detectados en la misma visita.</summary>
    public List<DiagnosticoEntry> Comorbilidades { get; set; } = [];

    /// <summary>Primario (si existe) + comorbilidades.</summary>
    public IEnumerable<DiagnosticoEntry> TodosDiagnosticos =>
        Diagnostico is null ? Comorbilidades : new[] { Diagnostico }.Concat(Comorbilidades);

    /// <summary>Categorías distintas que cubren todos los Dx; cae a Categoria si no hay Dx.</summary>
    public IReadOnlyList<string> Categorias =>
        TodosDiagnosticos.Any()
            ? TodosDiagnosticos.Select(d => d.Categoria).Distinct().ToList()
            : (string.IsNullOrEmpty(Categoria) ? [] : [Categoria]);
    public string Address1 { get; set; } = "";
    public string City { get; set; } = "";
    public bool EsNuevo { get; set; } = true;
    /// <summary>UUID de la visita creada en OpenMRS</summary>
    public string VisitUuid { get; set; } = "";
    /// <summary>UUID del encuentro ADULTINITIAL (ConsultaSeeder) — usado por LabOrderSeeder y PrescriptionSeeder</summary>
    public string ConsultaEncounterUuid { get; set; } = "";
    /// <summary>Datetime de la visita (fecha simulada + hora realista del día)</summary>
    public DateTime VisitDatetime { get; set; }
    /// <summary>
    /// Conceptos (fármacos/labs) ya ordenados para esta persona durante la simulación.
    /// Evita el AmbiguousOrderException de OpenMRS al re-ordenar lo mismo en visitas recurrentes.
    /// Las visitas recurrentes comparten esta colección con el paciente original.
    /// </summary>
    public HashSet<string> OrderedConcepts { get; set; } = [];
    /// <summary>Estación climática activa en la fecha de la visita (null si no hay catálogo de clima).</summary>
    public string? ClimaEstacion { get; set; }
    /// <summary>Temperatura ambiente promedio (°C) de la semana de la visita (null si no aplica).</summary>
    public double? TempAmbienteC { get; set; }
    /// <summary>Conceptos ya agregados a la lista de problemas (condition) — evita duplicados entre visitas.</summary>
    public HashSet<string> ProblemListConcepts { get; set; } = [];
    /// <summary>Consultorio (location) asignado a esta visita por ClinicResourceAssigner. Null = usar default.</summary>
    public string? AssignedLocationUuid { get; set; }
    /// <summary>Médico (provider) asignado a esta visita. Null = usar ProviderUuid por defecto.</summary>
    public string? AssignedProviderUuid { get; set; }
}
