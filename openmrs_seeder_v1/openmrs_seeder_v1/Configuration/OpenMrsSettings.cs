namespace OpenmrsSeeder.Configuration;

public class OpenMrsSettings
{
    public string SeedMode { get; set; } = "RestApi";
    public RestApiSettings RestApi { get; set; } = new();
    public DirectDbSettings DirectDb { get; set; } = new();
    public DefaultsSettings Defaults { get; set; } = new();
}

public class RestApiSettings
{
    public string BaseUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class DirectDbSettings
{
    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "openmrs";
    public string User { get; set; } = "openmrs";
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
    /// <summary>
    /// Consultorios disponibles (location + médico asignado). Si está vacío, el seeder usa
    /// LocationUuid/ProviderUuid (comportamiento previo). Cada visita rota entre estos consultorios.
    /// </summary>
    public List<ConsultorioSettings> Consultorios { get; set; } = [];
    /// <summary>UUID del tipo de visita "Outpatient" para visitas ambulatorias</summary>
    public string VisitTypeUuid { get; set; } = "7b0f5697-27e3-40c4-8bae-f4049abfb4ed";
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
}

/// <summary>
/// Un consultorio: su ubicación (Visit Location de OpenMRS) y el médico que lo atiende.
/// El médico se crea de forma idempotente al inicio de la corrida (ClinicResourceAssigner)
/// buscándolo por <see cref="MedicoIdentifier"/>; si no existe se crea con <see cref="MedicoNombre"/>.
/// </summary>
public class ConsultorioSettings
{
    /// <summary>UUID de la ubicación (Visit Location) del consultorio — GET /location.</summary>
    public string LocationUuid { get; set; } = "";
    /// <summary>Identificador único del proveedor/médico (idempotencia). Ej: "SIM-MED-C1".</summary>
    public string MedicoIdentifier { get; set; } = "";
    /// <summary>Nombre del médico a crear si no existe. Ej: "Carlos Médico".</summary>
    public string MedicoNombre { get; set; } = "";
}
