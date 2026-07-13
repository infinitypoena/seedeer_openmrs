namespace OpenmrsSeeder.Configuration;

public class OpenMrsSettings
{
    public RestApiSettings RestApi { get; set; } = new();
    public DefaultsSettings Defaults { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
}

/// <summary>
/// Acceso directo a MariaDB. El simulador siembra SOLO por REST; esta es la única excepción, y existe
/// porque OpenMRS sella cada fila con el reloj real del servidor: las fechas de negocio (visita, obs,
/// orden…) son las simuladas, pero las de auditoría (date_created) quedan todas el día de la corrida.
/// <see cref="OpenmrsSeeder.Services.AuditDateFixer"/> las retrofecha. Desactivado por defecto.
/// </summary>
public class DatabaseSettings
{
    /// <summary>Interruptor de la feature: false = no se toca la BD (el simulador sigue siendo REST puro).</summary>
    public bool CorregirFechas { get; set; }
    /// <summary>Cadena de conexión a MariaDB. Vacía = feature desactivada aunque CorregirFechas sea true.</summary>
    public string ConnectionString { get; set; } = "";
    /// <summary>Prefijo del identificador de los pacientes simulados: acota TODO lo que el proceso puede tocar.</summary>
    public string PrefijoPaciente { get; set; } = "SIM-";
    /// <summary>Filas por lote de UPDATE (transacciones cortas: ni undo log enorme ni bloqueos largos).</summary>
    public int TamanoLote { get; set; } = 20000;
    /// <summary>Pedir confirmación por consola antes de escribir. false = corrida desatendida.</summary>
    public bool PedirConfirmacion { get; set; } = true;

    /// <summary>La feature está operativa (activada Y con cadena de conexión).</summary>
    public bool Activo => CorregirFechas && !string.IsNullOrWhiteSpace(ConnectionString);
}

public class RestApiSettings
{
    public string BaseUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class DefaultsSettings
{
    /// <summary>UUID del tipo "OpenMRS ID" (required, con validador Luhn — idgen lo auto-genera al omitir identifier)</summary>
    public string PatientIdentifierTypeUuid { get; set; } = "05a29f94-c0ed-11e2-94be-8c13b969e334";
    /// <summary>UUID del tipo "Old Identification Number" (sin validador) — usado para el prefijo SIM- de tracking</summary>
    public string TrackingIdentifierTypeUuid { get; set; } = "8d79403a-c2cc-11de-8d13-0010c6dffd0f";
    /// <summary>UUID de la ubicación por defecto (fallback) — verificar en tu instancia via GET /location</summary>
    public string LocationUuid { get; set; } = "44c3efb0-2583-4c80-a79e-1f756a03c0a1";
    /// <summary>UUID de la ubicación de registro/admisión (Recepción) usada en el identificador del paciente. Si vacío, cae a LocationUuid.</summary>
    public string RegistrationLocationUuid { get; set; } = "";
    /// <summary>UUID del tipo de visita "OPD Visit" (consulta externa ambulatoria)</summary>
    public string VisitTypeUuid { get; set; } = "287463d3-2233-4c69-9851-5841a1f5e109";
    /// <summary>UUID del tipo de encuentro "Vitals"</summary>
    public string VitalsEncounterTypeUuid { get; set; } = "67a71486-1a54-468f-ac3e-7091a9a79584";
    /// <summary>UUID del tipo de encuentro "Consultation" (ADULTINITIAL)</summary>
    public string ConsultaEncounterTypeUuid { get; set; } = "92a52cce-c614-4046-b5f2-07f32f0bcf91";
    /// <summary>UUID del proveedor de salud usado como autor de los encuentros</summary>
    public string ProviderUuid { get; set; } = "f9badd80-ab76-11e2-9e96-0800200c9a66";
    /// <summary>UUID del rol "Clinician" en los encuentros — GET /ws/rest/v1/encounterrole</summary>
    public string EncounterRoleUuid { get; set; } = "240b26f9-dd88-4172-823d-4a8bfeb7841f";
    /// <summary>UUID del care setting "Outpatient" para órdenes — GET /ws/rest/v1/caresetting</summary>
    public string OutpatientCareSettingUuid { get; set; } = "6f0c9a92-6f24-11e3-af88-005056821db0";
    /// <summary>UUID de la frecuencia "ONCE DAILY" en OrderFrequency — GET /ws/rest/v1/orderfrequency</summary>
    public string OnceDailyFrequencyUuid { get; set; } = "38090760-b5e5-4b9e-8d1a-1e1d0b6e5aed";
    /// <summary>CIEL concept UUID para unidad "Days" (duración de prescripciones)</summary>
    public string DaysConceptUuid { get; set; } = "1072AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    /// <summary>CIEL concept UUID para unidad "Tablet(s)" (dosis de prescripciones)</summary>
    public string TabletConceptUuid { get; set; } = "1513AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    /// <summary>
    /// UUID del servicio de citas (Bahmni Appointments) para las citas de seguimiento —
    /// GET /ws/rest/v1/appointmentService/all/default. Vacío = no se crean citas (feature inactiva).
    /// </summary>
    public string AppointmentServiceUuid { get; set; } = "";
    /// <summary>UUID del tipo de servicio de la cita (p.ej. "Short follow-up"). Opcional.</summary>
    public string AppointmentServiceTypeUuid { get; set; } = "";
    /// <summary>UUID del person attribute type "Telephone Number" (String). Vacío = no se registra teléfono.</summary>
    public string TelephoneAttributeTypeUuid { get; set; } = "";
    /// <summary>
    /// UUID del person attribute type "Civil Status" (formato Concept: el value debe ser el UUID de
    /// un answer del concepto Estado civil 1054). Vacío = no se registra estado civil.
    /// </summary>
    public string CivilStatusAttributeTypeUuid { get; set; } = "";
    /// <summary>
    /// UUID del tipo de encuentro "Lab Results": el acto del laboratorio (toma de muestra + resultado),
    /// firmado por el técnico y no por el médico de la consulta. Vacío = no se crean encuentros de
    /// laboratorio (los resultados no se registran).
    /// </summary>
    public string LabResultsEncounterTypeUuid { get; set; } = "";
    /// <summary>UUID de la ubicación "Laboratorio". Vacío = cae al consultorio de la visita.</summary>
    public string LabLocationUuid { get; set; } = "";
}
