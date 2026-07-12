namespace OpenmrsSeeder.Configuration;

public class SimulationSettings
{
    public DateTime StartDate { get; set; } = new DateTime(2023, 1, 1);
    public DateTime EndDate { get; set; } = new DateTime(2024, 12, 31);
    public int PacientesPorDiaMedio { get; set; } = 40;
    public int PorcentajeRecurrentes { get; set; } = 30;
    /// <summary>
    /// Banda para el factor inicial de selección. Cada corrida sortea su probabilidad de "común"
    /// uniformemente en [CommonProbMin, CommonProbMax], así la proporción VARÍA entre corridas pero
    /// siempre se inclina a lo común. Por defecto ~75-95% (promedio ~85%).
    /// </summary>
    public double CommonProbMin { get; set; } = 0.75;
    public double CommonProbMax { get; set; } = 0.95;
    /// <summary>
    /// Banda de probabilidad de que un paciente recurrente sea atendido por su MISMO médico de
    /// cabecera (el de su primera visita), en su mismo consultorio. Cada corrida sortea su valor
    /// uniformemente en [Min, Max], inclinado al "sí" (def. 0.70–0.90). Los pacientes nuevos siempre
    /// estrenan médico de cabecera; con probabilidad (1-valor) un recurrente cae con otro médico.
    /// </summary>
    public double MedicoCabeceraProbMin { get; set; } = 0.70;
    public double MedicoCabeceraProbMax { get; set; } = 0.90;
    /// <summary>
    /// Médicos que "abren consultorio" cada día: cada día se sortea un tamaño en [Min, Max]
    /// (recortado al pool disponible) y solo esos médicos atienden. Modela una clínica pequeña
    /// donde no siempre están todos. Def. 2–3. Si el pool ≤ Min, todos disponibles (sin efecto).
    /// </summary>
    public int MinMedicosPorDia { get; set; } = 2;
    public int MaxMedicosPorDia { get; set; } = 3;
    /// <summary>
    /// Probabilidad de que una visita recurrente de un paciente con condición crónica conocida sea un
    /// control de esa MISMA condición (continuidad longitudinal) en lugar de un motivo agudo nuevo y
    /// aleatorio. Solo aplica si el paciente ya arrastra ≥1 diagnóstico crónico. Def. 0.70.
    /// </summary>
    public double SeguimientoCronicoProb { get; set; } = 0.70;
    /// <summary>
    /// Prob. de que un recurrente NO crónico que vuelve dentro de la ventana de su episodio agudo
    /// regrese por el MISMO dx (control/evolución) en vez de una enfermedad aleatoria nueva.
    /// </summary>
    public double SeguimientoAgudoProb { get; set; } = 0.70;
    /// <summary>Días de vigencia del episodio agudo desde su última visita (def. 30).</summary>
    public int VentanaSeguimientoAgudoDias { get; set; } = 30;
    public string Locale { get; set; } = "es";
    /// <summary>
    /// Offset UTC de las fechas enviadas a OpenMRS ("±HH:mm", p.ej. "-06:00" El Salvador).
    /// Debe coincidir con la TZ del backend para que las horas se lean como hora local.
    /// Vacío = UTC (comportamiento histórico). ⚠️ Cambiarlo desalinea los datos ya insertados
    /// con el offset anterior — pensado para aplicarse antes de regenerar.
    /// </summary>
    public string UtcOffset { get; set; } = "";
    public int RandomSeed { get; set; } = 42;
    public HorarioAtencionSettings HorarioAtencion { get; set; } = new();
    public DemographicProfileSettings DemographicProfile { get; set; } = new();
    public ReferralProbabilitiesSettings ReferralProbabilities { get; set; } = new();
    public WeekdayWeightsSettings WeekdayWeights { get; set; } = new();
    public ComorbiditySettings Comorbidity { get; set; } = new();
    public ClimateSettings Climate { get; set; } = new();
    public AllergySettings Allergy { get; set; } = new();
    public RecurrenceSettings Recurrence { get; set; } = new();
    public AppointmentsSettings Appointments { get; set; } = new();
    public VariedadSettings Variedad { get; set; } = new();
    public OrdersSettings Orders { get; set; } = new();
}

/// <summary>Parámetros de las órdenes clínicas (labs y prescripciones).</summary>
public class OrdersSettings
{
    /// <summary>
    /// Días de vigencia de una orden de laboratorio (su <c>autoExpireDate</c>). Mientras la orden esté
    /// vigente, el mismo test no se vuelve a pedir (evita el AmbiguousOrderException); pasado ese plazo,
    /// un control crónico puede re-ordenarlo. Las prescripciones expiran solas por su <c>duration</c>.
    /// </summary>
    public int LabVigenciaDias { get; set; } = 7;
}

/// <summary>
/// Variación de diagnósticos dentro de una corrida. Sin esto, los dx de mayor peso acaparan la
/// selección y la cola larga del catálogo (~950 dx) apenas se explora.
/// </summary>
public class VariedadSettings
{
    /// <summary>
    /// Amortiguación anti-repetición: cada vez que un dx sale en la corrida su peso efectivo baja
    /// (peso / (1 + damping × usos)), empujando la selección hacia diagnósticos aún no vistos.
    /// No altera el perfil epidemiológico (la categoría se sortea igual por edad/sexo/clima) ni las
    /// visitas de control crónico/agudo (repiten dx a propósito, fuera del selector). 0 = desactivado.
    /// </summary>
    public double RepeticionDamping { get; set; } = 0.25;
}

/// <summary>
/// Parámetros de las citas reales (módulo Bahmni Appointments). La cita se crea cuando dispara el
/// FollowUp existente (la cita ES el seguimiento materializado); aquí solo va la resolución.
/// </summary>
public class AppointmentsSettings
{
    /// <summary>
    /// Días de tolerancia para considerar CUMPLIDA una cita pendiente cuando el paciente vuelve
    /// (|fecha cita − fecha visita| ≤ tolerancia → Completed; anterior a la ventana → Missed).
    /// </summary>
    public int ToleranciaDias { get; set; } = 3;
    /// <summary>
    /// Probabilidad de que un paciente con cita de control para hoy (±tolerancia) efectivamente asista.
    /// El resto son no-shows: su cita queda pendiente y, al vencer, se resuelve como Missed. Def. 0,75.
    /// </summary>
    public double AsistenciaProb { get; set; } = 0.75;
}

/// <summary>
/// Intervalo mínimo clínicamente plausible entre dos visitas del mismo paciente. Evita que un
/// recurrente vuelva a consulta externa día tras día. La banda depende de si el paciente arrastra una
/// condición crónica (control mensual/trimestral) o no (seguimiento agudo de 1–3 semanas).
/// </summary>
public class RecurrenceSettings
{
    /// <summary>Días mínimos para volver si NO es crónico (seguimiento agudo).</summary>
    public int MinDiasAgudo { get; set; } = 7;
    /// <summary>Días máximos del seguimiento agudo.</summary>
    public int MaxDiasAgudo { get; set; } = 21;
    /// <summary>Días mínimos para el control de una condición crónica.</summary>
    public int MinDiasCronico { get; set; } = 30;
    /// <summary>Días máximos del control crónico.</summary>
    public int MaxDiasCronico { get; set; } = 120;
}

public class AllergySettings
{
    /// <summary>
    /// Banda de prevalencia por corrida (P de que un paciente nuevo tenga ≥1 alergia documentada).
    /// Cada corrida sortea su valor uniformemente en [Min, Max], igual que CommonProbMin/Max, así
    /// la proporción de alérgicos VARÍA entre corridas. Por defecto ~15-25% (dato OMS/SEAIC: el
    /// 25-30% de la población tiene alguna alergia; aquí se modela la fracción clínicamente documentada).
    /// </summary>
    public double BaseProbabilityMin { get; set; } = 0.15;
    public double BaseProbabilityMax { get; set; } = 0.25;
    /// <summary>Prob. condicional de sumar una 2ª alergia dado que ya tiene 1.</summary>
    public double SecondAllergyProbability { get; set; } = 0.30;
    /// <summary>Prob. condicional de sumar una 3ª alergia dado que ya tiene 2.</summary>
    public double ThirdAllergyProbability { get; set; } = 0.25;
    /// <summary>Tope absoluto de alergias por paciente.</summary>
    public int MaxAllergies { get; set; } = 3;
}

public class ClimateSettings
{
    /// <summary>Si es false, el catálogo clima.csv se ignora aunque exista.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Multiplicador de peso para enfermedades/categorías favorecidas por el clima activo.</summary>
    public double SeasonalBoost { get; set; } = 2.5;
    /// <summary>Temperatura de confort (°C) de referencia para el ajuste de signos vitales.</summary>
    public double ComfortTempC { get; set; } = 24.0;
    /// <summary>°C de temperatura corporal añadidos por cada °C ambiente por encima del confort.</summary>
    public double TempVitalsFactorC { get; set; } = 0.04;
    /// <summary>Tope del ajuste de temperatura corporal por calor ambiental (°C).</summary>
    public double TempVitalsMaxC { get; set; } = 0.5;
}

public class ComorbiditySettings
{
    /// <summary>Probabilidad base de que un paciente tenga ≥1 comorbilidad.</summary>
    public double BaseProbability { get; set; } = 0.20;
    /// <summary>Tope de diagnósticos adicionales (además del primario).</summary>
    public int MaxAdditional { get; set; } = 2;
    /// <summary>Probabilidad de añadir una 2ª comorbilidad cuando ya hay una.</summary>
    public double SecondExtraProbability { get; set; } = 0.25;
    /// <summary>Multiplicador de peso aplicado a categorías clínicamente afines.</summary>
    public double AffinityBoost { get; set; } = 4.0;
    /// <summary>Multiplicador de la probabilidad base por grupo de edad.</summary>
    public Dictionary<string, double> AgeScaling { get; set; } = new()
    {
        ["0-14"] = 0.3, ["15-29"] = 0.5, ["30-44"] = 0.8, ["45-64"] = 1.3, ["65+"] = 1.8
    };
    // Las afinidades (clusters de comorbilidad) viven ahora en catalogs/comorbilidad_afinidades.csv
}

public class HorarioAtencionSettings
{
    public BloquePico PicoAM { get; set; } = new() { Inicio = "08:00", Fin = "10:00", Peso = 40 };
    public BloquePico PicoPM { get; set; } = new() { Inicio = "14:00", Fin = "16:00", Peso = 30 };
}

public class BloquePico
{
    public string Inicio { get; set; } = "";
    public string Fin { get; set; } = "";
    public int Peso { get; set; }
}

public class DemographicProfileSettings
{
    public List<AgeGroupWeight> AgeGroups { get; set; } =
    [
        new() { Label = "0-14",  Weight = 20 },
        new() { Label = "15-29", Weight = 18 },
        new() { Label = "30-44", Weight = 25 },
        new() { Label = "45-64", Weight = 25 },
        new() { Label = "65+",   Weight = 12 },
    ];

    public GenderRatioSettings GenderRatio { get; set; } = new();

    /// <summary>Edad mínima general de los pacientes generados, en meses.</summary>
    public int MinPatientAgeMonths { get; set; } = 6;
    /// <summary>Consultorio pediátrico: si es true, el mínimo baja a PediatricMinAgeMonths.</summary>
    public bool PediatricClinic { get; set; } = false;
    /// <summary>Edad mínima en meses cuando PediatricClinic = true.</summary>
    public int PediatricMinAgeMonths { get; set; } = 1;
}

public class AgeGroupWeight
{
    public string Label { get; set; } = "";
    public int Weight { get; set; }
}

public class GenderRatioSettings
{
    public int M { get; set; } = 48;
    public int F { get; set; } = 52;
}

public class ReferralProbabilitiesSettings
{
    public double LabOrder { get; set; } = 0.40;
    public double ClinicalExam { get; set; } = 0.35;
    public double DrugOrder { get; set; } = 0.65;
    public double Urgent { get; set; } = 0.20;
    public double FollowUp { get; set; } = 0.30;
    /// <summary>Prob. de agendar control cuando el cuadro incluye una condición crónica. Def. 0,90.</summary>
    public double FollowUpCronico { get; set; } = 0.90;
    /// <summary>Prob. de agendar control cuando el cuadro (no crónico) es grave. Def. 0,80.</summary>
    public double FollowUpGrave { get; set; } = 0.80;
    /// <summary>
    /// Fracción de órdenes de laboratorio que "vuelven" con un resultado el mismo día (obs ligada a
    /// la orden). El resto queda pendiente (sin resultado), como en una clínica real. Def. 0.90.
    /// Solo aplica a tests numéricos/codificados; paneles e imágenes nunca registran valor en v1.
    /// </summary>
    public double LabResult { get; set; } = 0.90;
}

public class WeekdayWeightsSettings
{
    public double Monday { get; set; } = 1.20;
    public double Tuesday { get; set; } = 1.20;
    public double Wednesday { get; set; } = 1.00;
    public double Thursday { get; set; } = 1.00;
    public double Friday { get; set; } = 0.90;
    public double Saturday { get; set; } = 0.50;
    public double Sunday { get; set; } = 0.00;
}
