using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Models.Simulation;

/// <summary>Cita agendada aún sin resolver (Scheduled): UUID en OpenMRS + fecha programada.</summary>
public readonly record struct CitaPendiente(string Uuid, DateTime Fecha);

/// <summary>
/// Resultado de laboratorio que "aún no llegó": la orden existe pero el valor se registrará en la
/// siguiente visita del paciente (retraso realista). Numérico/codificado excluyentes; los paneles
/// llevan la lista de componentes (obs-group). Se genera al ordenar (con el contexto clínico de esa
/// visita) y se postea al volver.
/// </summary>
public sealed record ResultadoPendiente(
    string OrderUuid,
    string ConceptUuid,
    double? Numerico,
    string? CodedUuid,
    List<(string ConceptUuid, double Valor)>? Componentes);

public class SimulatedPatient
{
    public string Identifier { get; set; } = "";
    public string OpenMrsUuid { get; set; } = "";
    public string GivenName { get; set; } = "";
    /// <summary>Segundo nombre (opcional). Se envía a OpenMRS como <c>middleName</c>.</summary>
    public string SecondGivenName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    /// <summary>Segundo apellido (opcional). Se envía a OpenMRS como <c>familyName2</c>.</summary>
    public string SecondFamilyName { get; set; } = "";
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
    /// <summary>Departamento (state_province). Vacío = fallback Bogus sin catálogo de direcciones.</summary>
    public string StateProvince { get; set; } = "";
    /// <summary>País de residencia. Vacío = comportamiento histórico ("España" en PatientSeeder).</summary>
    public string Country { get; set; } = "";
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
    /// <summary>
    /// UUIDs de programas de atención en los que el paciente ya está inscrito — evita reinscribirlo
    /// entre visitas recurrentes. Compartida por referencia con la copia recurrente (como
    /// <see cref="ProblemListConcepts"/>).
    /// </summary>
    public HashSet<string> EnrolledPrograms { get; set; } = [];
    /// <summary>
    /// Fecha de retorno decidida en la consulta de ESTA visita (obs "Return visit date").
    /// La consume <c>AppointmentSeeder</c> para agendar la cita real. Null = sin seguimiento.
    /// </summary>
    public DateTime? FechaSeguimiento { get; set; }
    /// <summary>
    /// Citas agendadas aún en estado Scheduled. Al volver el paciente se resuelven (Completed si
    /// cae cerca de la fecha, Missed si ya venció). Compartida por referencia con la copia
    /// recurrente (como <see cref="ProblemListConcepts"/>).
    /// </summary>
    public List<CitaPendiente> CitasPendientes { get; set; } = [];
    /// <summary>Resultados de laboratorio pendientes de llegar (compartida por referencia, como CitasPendientes).</summary>
    public List<ResultadoPendiente> ResultadosPendientes { get; set; } = [];
    /// <summary>
    /// Diagnósticos crónicos que arrastra el paciente (los <c>EsCronica</c> ya asignados en visitas previas).
    /// Las visitas recurrentes vuelven a uno de estos como motivo de control con alta probabilidad.
    /// Compartida por referencia con la copia recurrente (como <see cref="ProblemListConcepts"/>).
    /// </summary>
    public List<DiagnosticoEntry> CronicasActivas { get; set; } = [];
    /// <summary>
    /// Fecha más temprana en que el paciente puede volver a consulta (intervalo mínimo entre visitas).
    /// Se fija tras cada visita con <see cref="Services.RecurrenceScheduler"/>; <c>null</c> = elegible ya.
    /// </summary>
    public DateOnly? ProximoElegibleDesde { get; set; }
    /// <summary>
    /// Episodio agudo abierto (dx primario NO crónico de la última visita). Si el paciente vuelve
    /// dentro de la ventana (<c>VentanaSeguimientoAgudoDias</c>), con alta probabilidad regresa por
    /// este mismo dx (control) en vez de una enfermedad aleatoria. Se cierra tras su visita de control.
    /// Solo vive en el objeto del pool (se escribe vía <c>SeedOrchestrator.RegistrarEpisodioAgudo</c>).
    /// </summary>
    public DiagnosticoEntry? UltimoDxAgudo { get; set; }
    /// <summary>Fecha de la visita que abrió/renovó el episodio agudo.</summary>
    public DateOnly? FechaUltimoDxAgudo { get; set; }
    /// <summary>Consultorio (location) asignado a esta visita por ClinicResourceAssigner. Null = usar default.</summary>
    public string? AssignedLocationUuid { get; set; }
    /// <summary>Médico (provider) asignado a esta visita. Null = usar ProviderUuid por defecto.</summary>
    public string? AssignedProviderUuid { get; set; }
    /// <summary>Consultorio del médico de cabecera (fijado en la primera visita). Las visitas recurrentes lo heredan.</summary>
    public string? CabeceraLocationUuid { get; set; }
    /// <summary>Médico de cabecera del paciente (el de su primera visita). Las recurrentes vuelven a él con alta probabilidad.</summary>
    public string? CabeceraProviderUuid { get; set; }
}
