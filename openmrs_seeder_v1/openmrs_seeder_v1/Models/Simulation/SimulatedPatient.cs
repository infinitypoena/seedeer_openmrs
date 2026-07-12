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
    /// <summary>Teléfono salvadoreño sintético (7###-#### móvil / 2###-#### fijo). Vacío = sin atributo.</summary>
    public string Telefono { get; set; } = "";
    /// <summary>UUID del answer de Estado civil (concepto 1054) coherente con la edad. Vacío = sin atributo.</summary>
    public string EstadoCivilUuid { get; set; } = "";
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
    /// Talla (cm) fijada en la primera visita y constante de por vida (un adulto no cambia de estatura
    /// entre visitas; un niño crece por su curva de edad, pero su talla base se ancla aquí). Heredada por
    /// la copia recurrente. <c>null</c> = aún no calculada.
    /// </summary>
    public double? TallaCm { get; set; }
    /// <summary>
    /// IMC basal (constitución) fijado en la primera visita. En visitas siguientes el peso deriva poco
    /// alrededor de este valor (salvo efecto puntual de enfermedad), evitando pesos incoherentes entre
    /// controles. <c>null</c> = aún no calculado.
    /// </summary>
    public double? ImcBasal { get; set; }
    /// <summary>
    /// Conceptos (fármacos/labs) ordenados para esta persona → fecha hasta la que la orden sigue ACTIVA
    /// (vigencia: <c>autoExpireDate</c> del lab o <c>dateActivated + duración</c> del fármaco). Evita el
    /// AmbiguousOrderException de OpenMRS solo mientras la orden vive; pasada la vigencia, un control
    /// crónico vuelve a ordenar el mismo lab/fármaco (ver <see cref="Services.OrderVigencia"/>).
    /// Las visitas recurrentes comparten esta colección por referencia con el paciente original.
    /// </summary>
    public Dictionary<string, DateOnly> OrderedConcepts { get; set; } = [];
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
    /// Cuando la consulta agenda un control, coincide con <see cref="ProximaCita"/>.
    /// </summary>
    public DateOnly? ProximoElegibleDesde { get; set; }
    /// <summary>
    /// Fecha de la cita de control agendada en la última visita (<c>null</c> = no se agendó seguimiento).
    /// La consume <see cref="Services.RecurrentSelector"/> para priorizar el retorno del MISMO paciente
    /// en su fecha, en vez de elegir recurrentes al azar. Es el estado interno de la agenda: gobierna el
    /// retorno aunque la feature de citas reales de OpenMRS (<c>AppointmentServiceUuid</c>) esté inactiva.
    /// </summary>
    public DateOnly? ProximaCita { get; set; }
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
