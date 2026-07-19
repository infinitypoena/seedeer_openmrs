namespace OpenmrsSeeder.Configuration;

public class SimulationSettings
{
    public DateTime StartDate { get; set; } = new DateTime(2023, 1, 1);
    public DateTime EndDate { get; set; } = new DateTime(2024, 12, 31);
    /// <summary>
    /// Pacientes/día con los que ARRANCA la clínica: el día 0 el pool está vacío, así que son
    /// <b>todos altas nuevas</b>. Con el crecimiento activo es el punto de partida de la curva (de aquí se
    /// deriva <c>p</c>), no una constante.
    ///
    /// <para>⚠️ Ya <b>no</b> existe <c>PorcentajeRecurrentes</c>. El volumen del día era
    /// <c>altas / (1 − PorcentajeRecurrentes)</c> y los recurrentes un residuo fijo del 30 %, con lo que la
    /// demanda real del panel no pintaba nada: la mitad de las citas de control vencían sin que nadie las
    /// atendiera. Ahora <c>visitas = altas + retornos</c> (una suma), los retornos los cuenta
    /// <c>RecurrentSelector</c> sobre el pool de verdad y su fracción <b>emerge</b> y crece con el panel.
    /// Ver <c>leyes_simulacion.md</c>.</para>
    /// </summary>
    public int PacientesPorDiaMedio { get; set; } = 6;
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
    public LaboratorioSettings Laboratorio { get; set; } = new();
    public SatisfaccionSettings Satisfaccion { get; set; } = new();
    public ChequeoSettings Chequeo { get; set; } = new();
    public CrecimientoSettings Crecimiento { get; set; } = new();
    public SalidaSettings Salida { get; set; } = new();
}

/// <summary>
/// Calificación (1-5) que el paciente le pone a cada visita. No es un dato clínico: no se escribe en
/// OpenMRS, vive en el pool y se persiste en los CSV de salida. Alimenta al modelo de crecimiento
/// (<see cref="CrecimientoSettings"/>): solo los satisfechos recomiendan la clínica.
///
/// La nota se compone de una media base más modificadores del contexto REAL de la visita (le atendió
/// su médico de cabecera, acudió a una cita agendada, el cuadro era grave) y se penaliza por la
/// saturación del día. Esa penalización es el <b>freno del sistema</b>: crecer llena la consulta, la
/// consulta llena atiende peor, y peor atención frena el boca a boca.
/// </summary>
public class SatisfaccionSettings
{
    /// <summary>Si es false, no se califica ninguna visita (nadie queda insatisfecho y el crecimiento se apoya solo en el término de innovación).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Nota media de una visita neutra (sin bonus, sin penalizaciones). En la escala 1-5.</summary>
    public double MediaBase { get; set; } = 4.0;
    /// <summary>Desviación típica del ruido normal que se suma a la nota (dispersión entre pacientes).</summary>
    public double Desviacion { get; set; } = 0.8;
    /// <summary>Puntos que suma la continuidad asistencial: le atendió SU médico de cabecera.</summary>
    public double BonusMedicoCabecera { get; set; } = 0.4;
    /// <summary>Puntos que suma acudir a una cita agendada (le esperaban) en vez de llegar sin cita.</summary>
    public double BonusCitaCumplida { get; set; } = 0.2;
    /// <summary>Puntos que resta un cuadro grave (la experiencia de una consulta por algo serio es peor).</summary>
    public double PenalizacionCuadroGrave { get; set; } = 0.3;
    /// <summary>
    /// Puntos que resta una saturación de 1,0 (el doble de pacientes que la capacidad cómoda). El
    /// descuento es proporcional: <c>PenalizacionSaturacion × saturacion(d)</c>.
    /// </summary>
    public double PenalizacionSaturacion { get; set; } = 1.2;
    /// <summary>
    /// Pacientes que la clínica atiende en un día sin que se note la espera. A partir de aquí la
    /// saturación crece y las notas bajan. Ponerlo ≈ <c>Crecimiento.PacientesPorDiaObjetivo</c>: así, en la
    /// meseta la consulta va cómoda y solo los días punta pasan factura. Muy por debajo del objetivo, la
    /// clínica vive saturada, las notas se hunden y el churn se dispara.
    /// </summary>
    public int CapacidadComodaPorDia { get; set; } = 25;
    /// <summary>
    /// Promedio de calificaciones por encima del cual el paciente se considera satisfecho (estricto).
    /// El insatisfecho deja de recomendar la clínica y casi deja de acudir a sus citas.
    /// </summary>
    public double UmbralSatisfaccion { get; set; } = 3.0;
}

/// <summary>
/// Crecimiento de la clínica por <b>difusión de Bass</b> (1969), el modelo estándar de adopción por
/// boca a boca. La llegada de pacientes NUEVOS por día es:
/// <code>
/// λ(d) = [ p + q · S(d)/M ] · ( M − A(d) )
/// </code>
/// con <c>M</c> = <see cref="PoblacionCaptacion"/>, <c>A(d)</c> = clientela activa,
/// <c>S(d)</c> = recurrentes activos y satisfechos, <c>q</c> = coeficiente de imitación (boca a boca) y
/// <c>p</c> = coeficiente de innovación (los que llegan solos).
///
/// <b>Ni <c>p</c> ni <c>q</c> se configuran: se derivan</b> de los dos extremos de la curva —<c>p</c> del
/// arranque, <c>q</c> del objetivo—. Son coeficientes con los que nadie puede apuntar a ojo; los extremos
/// de la curva, en cambio, son números que el usuario sí entiende.
///
/// <b>La curva se gobierna con tres mandos, y significan exactamente lo que dicen:</b>
/// <list type="bullet">
/// <item><c>PacientesPorDiaMedio</c> — <b>dónde arranca</b> la clínica. De ahí se deriva <c>p</c>.</item>
/// <item><see cref="PacientesPorDiaObjetivo"/> — <b>dónde se estabiliza</b>. De ahí se deriva <c>q</c>.</item>
/// <item><see cref="VentanaActividadDias"/> — <b>cuánto tarda</b> en llegar.</item>
/// </list>
/// </summary>
public class CrecimientoSettings
{
    /// <summary>Si es false, el volumen diario sale del plan precalculado de siempre (media fija).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// <b>Dónde se estabiliza la clínica</b>, en <b>visitas totales</b>/día (altas + controles) — que es lo
    /// mismo que decir <b>su capacidad de trabajo</b>: el simulador lo usa también como <b>aforo</b>
    /// (<c>BassGrowthModel.CapacidadDelDia</c>). Por eso <c>Satisfaccion.CapacidadComodaPorDia</c> debe
    /// valer lo mismo. De aquí se deriva la fuerza del boca a boca por bisección
    /// (<c>BassGrowthModel.CalibrarImitacion</c>), igual que <c>PacientesPorDiaMedio</c> fija el arranque.
    ///
    /// <para>Cuando la demanda del día lo supera, <b>lo que se recorta son las altas</b>: una consulta llena
    /// deja de captar gente nueva, no le da plantón al crónico que tenía cita. Ese recorte es, además, el
    /// freno al crecimiento más honesto que tiene el modelo.</para>
    ///
    /// <para>⚠️ Antes el pomo era <see cref="RecurrentesPorPacienteExtra"/> a pelo, y era <b>inservible</b>:
    /// el punto fijo del lazo vale <c>1/(1 − ganancia)</c> y explota al acercarse a 1 — entre 1/25 y 1/8
    /// (un factor 3) el resultado saltaba de crecer un 60 % a estrellarse contra el techo en 12 meses.
    /// Nadie puede apuntar con eso. Ahora se apunta al destino y el modelo calcula el resto.</para>
    ///
    /// <para>⚠️ El <b>suelo</b> no es <c>PacientesPorDiaMedio</c>: el panel por sí solo ya produce
    /// <c>PacientesPorDiaMedio × 1/(1−k)</c> visitas al día (cada paciente vuelve varias veces). Si el
    /// objetivo no supera ese suelo, la clínica no necesita boca a boca y <c>q</c> se deriva a 0.</para>
    /// </summary>
    public int PacientesPorDiaObjetivo { get; set; } = 25;
    /// <summary>
    /// <b>Cuánto tarda</b> la clínica en llegar a su meseta: son los días que un paciente sigue contando
    /// como clientela activa (y por tanto sigue recomendándola) desde su última visita. Es la memoria del
    /// boca a boca, y con ella se estira o se comprime la rampa. Con objetivo 25 y arranque 6:
    /// 90 d → la meseta llega en ~1 año; <b>365 d → el crecimiento se reparte por los 3 años</b> (arranque
    /// lento, rampa sostenida, estabilidad al final).
    ///
    /// <para>Es también lo que impide que S(d) sea un contador acumulado que solo sube: el que se alejó
    /// deja de hablar de la clínica.</para>
    /// </summary>
    public int VentanaActividadDias { get; set; } = 365;
    /// <summary>
    /// <c>M</c> — población del área de influencia. Guardarraíl: cuando la clientela activa se acerca a M
    /// ya no queda a quién captar y el crecimiento se apaga.
    ///
    /// <para>⚠️ Debe ser <b>holgado</b> respecto a lo que la clínica va a captar en la ventana. Con 20.000
    /// y el escenario real (6 → 25 pac/día en 3,5 años) la clínica acababa registrando 22.000 pacientes
    /// distintos: <b>más gente de la que vive en el barrio</b>. El área se agotaba, la curva se desinflaba
    /// al final y la calibración se distorsionaba. A volúmenes sanos este freno <b>apenas actúa</b> (la
    /// clientela ronda el 5-10 % del área): quien fija la meseta es el objetivo.</para>
    /// </summary>
    public int PoblacionCaptacion { get; set; } = 60000;
    /// <summary>
    /// <b>Override avanzado.</b> Fija a mano los recurrentes satisfechos que hacen falta para traer +1
    /// paciente nuevo al día (el inverso del coeficiente de imitación: 25 → <c>q = 0,04</c>). Gana sobre
    /// <see cref="PacientesPorDiaObjetivo"/>.
    ///
    /// <para><b>0 (por defecto) = derivar del objetivo</b>, que es lo que quieres el 99 % de las veces:
    /// este número es inestable y no se puede apuntar a ojo.</para>
    /// </summary>
    public int RecurrentesPorPacienteExtra { get; set; } = 0;
    /// <summary>
    /// Red de seguridad: tope absoluto de pacientes atendidos en un día. Debe quedar <b>por encima</b> del
    /// objetivo, que es quien fija el aforo de verdad; esto solo evita una explosión si se desconfigura algo.
    ///
    /// <para>⚠️ Cuando el objetivo no se respetaba, era ESTE número el que acababa gobernando la clínica:
    /// en la corrida de 3,5 años la media diaria se clavó en 45 (aquí) durante 33 de los 42 meses, con un
    /// objetivo de 25. Un techo de seguridad que muerde todos los días no es un techo de seguridad: es el
    /// modelo. La ley L5 (<c>Invariantes</c>) vigila justamente eso.</para>
    /// </summary>
    public int PacientesPorDiaMax { get; set; } = 45;
    /// <summary>Visitas mínimas para contar como "recurrente" a efectos del boca a boca (2 = ya volvió una vez).</summary>
    public int MinVisitasRecurrente { get; set; } = 2;
    /// <summary>
    /// Probabilidad de que un paciente INSATISFECHO acuda a su cita de control (frente a
    /// <c>Appointments.AsistenciaProb</c> para los satisfechos). Además queda fuera del relleno
    /// aleatorio de recurrentes: es el churn, y sus citas acaban en Missed.
    /// </summary>
    public double AsistenciaProbInsatisfecho { get; set; } = 0.20;
}

/// <summary>Artefactos de salida de la corrida (evidencia en disco; no tocan OpenMRS).</summary>
public class SalidaSettings
{
    /// <summary>
    /// Carpeta donde se escriben <c>crecimiento_diario.csv</c>, <c>clientes_recurrentes.csv</c> y el log de
    /// la corrida. Si es relativa, cuelga de la carpeta del binario. Vacía = no se escribe nada.
    /// </summary>
    public string Carpeta { get; set; } = "output";

    /// <summary>
    /// Volcar a <c>output/corrida_&lt;fecha&gt;.log</c> <b>todo lo que sale por consola</b> (las 5 etapas, el
    /// progreso, los errores), para poder auditarlo después con calma. Una corrida de 3,5 años escupe miles
    /// de líneas y la terminal no las guarda.
    /// </summary>
    public bool ArchivoLog { get; set; } = true;

    /// <summary>
    /// Volcar a <c>output/errores.csv</c> una fila por cada error de operación e ítem perdido (timestamp,
    /// componente, tipo, mensaje completo). El <c>.log</c> los tiene pero mezclados con miles de líneas
    /// INFO; el resumen de consola los trunca y capa a 100 — este CSV es la vista navegable.
    /// </summary>
    public bool ArchivoErrores { get; set; } = true;
}

/// <summary>
/// Ciclo de vida de la orden de laboratorio (<c>Order.fulfillerStatus</c>): cuándo se toma la muestra,
/// cuándo se rechaza y cuándo llega el resultado. Es lo que llena la cola de la app de laboratorio de O3.
/// Qué examen se hace en la clínica y cuánto tarda cada uno sale del catálogo (<c>laboratorios.csv</c>:
/// <c>se_realiza_en_clinica</c>, <c>dias_entrega_min/max</c>), no de aquí.
/// </summary>
public class LaboratorioSettings
{
    /// <summary>
    /// Fracción de muestras que el laboratorio rechaza (hemolizada, insuficiente, el paciente no acudió
    /// a la toma): la orden queda en <c>DECLINED</c> con el motivo y sin resultado. Def. 0.04.
    /// </summary>
    public double ProbRechazo { get; set; } = 0.04;
    /// <summary>
    /// Fracción de muestras tomadas cuyo resultado acaba llegando. El resto se pierde y su orden se queda
    /// en curso (<c>IN_PROGRESS</c>) para siempre, como en una clínica real. Def. 0.95.
    /// </summary>
    public double ProbResultadoLlega { get; set; } = 0.95;
    /// <summary>Minutos entre la consulta y la toma de la muestra (el paciente pasa por el laboratorio).</summary>
    public int MinutosHastaTomaMin { get; set; } = 20;
    public int MinutosHastaTomaMax { get; set; } = 90;
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
    public double RepeticionDamping { get; set; } = 0.10;
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
    /// <summary>
    /// Banda del <b>control post-alta</b>: cuándo vuelve el paciente al que hoy se ha referido al
    /// hospital. Propia a propósito — con la banda aguda (7-21 d) aún estaría ingresado, y con la crónica
    /// (30-120 d) se vería demasiado tarde cómo salió.
    /// </summary>
    public int MinDiasPostReferencia { get; set; } = 15;
    public int MaxDiasPostReferencia { get; set; } = 30;

    /// <summary>
    /// <b>Veces al año que un paciente del panel vuelve POR SU CUENTA</b>, sin que nadie le haya citado:
    /// le pasa algo nuevo meses después y se acuerda de la clínica. Es la segunda vía de retorno, junto a
    /// la cita de control, y es la que hace que el pool de pacientes <b>se use</b> — "coger a cualquiera de
    /// la lista al azar", pero como <b>tasa por paciente</b>, no rellenando un cupo.
    ///
    /// <para>Solo aplica a los pacientes <b>activos</b> (última visita dentro de
    /// <c>Crecimiento.VentanaActividadDias</c>) y que ya cumplieron su intervalo mínimo entre visitas. El
    /// paciente insatisfecho no vuelve solo. Internamente se convierte a probabilidad diaria
    /// (<c>RecurrentSelector.ProbRetornoEspontaneoDiaria</c>): nadie sabe apuntar un 0,0014 diario, pero
    /// todo el mundo sabe decir "por aquí pasa por su cuenta como una vez cada dos años".</para>
    ///
    /// <para>Es el mando que fija <b>cuánta consulta genera el panel por sí solo</b> y, con él, la fracción
    /// de recurrentes en régimen. 0 = solo se vuelve si hay cita (el panel no genera consulta espontánea).
    /// </para>
    /// </summary>
    public double VisitasEspontaneasPorPacienteAno { get; set; } = 0.5;
}

/// <summary>
/// El paciente que pide exámenes <b>por su propia iniciativa</b>. Dos escenarios: la visita de
/// <b>chequeo puro</b> (viene sano, "a ver si no hay nada mal": sin diagnóstico, solo labs comunes del
/// pool <c>chequeo=true</c> de laboratorios.csv) y el <b>examen adicional a petición</b> (un enfermo que
/// aprovecha su consulta para pedir un examen común extra). Todas las tiradas usan un RNG propio
/// (<c>RandomSeed + 21</c> en el orquestador, <c>+24</c> en LabOrderSeeder): con <c>Enabled=false</c>
/// el flujo aleatorio histórico queda intacto.
/// No toca volumen/recurrencia/crecimiento (cambia el CONTENIDO de la visita, no su existencia).
/// </summary>
public class ChequeoSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Prob. de que un paciente NUEVO (alta del día) venga a chequeo en vez de enfermo. Def. 0,04.</summary>
    public double ProbabilidadAlta { get; set; } = 0.04;
    /// <summary>
    /// Prob. de que un retorno ESPONTÁNEO (sin cita ni motivo de control) sea un chequeo. Nunca aplica
    /// a una cita agendada ni a un control de crónica/episodio agudo. Def. 0,02.
    /// </summary>
    public double ProbabilidadRetornoEspontaneo { get; set; } = 0.02;
    /// <summary>Prob. de que un paciente ENFERMO pida además un examen común por su cuenta. Def. 0,05.</summary>
    public double ProbExamenAdicional { get; set; } = 0.05;
    /// <summary>Prob. de agendar seguimiento tras un chequeo (un sano casi nunca sale citado). Def. 0,05.</summary>
    public double FollowUp { get; set; } = 0.05;
    /// <summary>Cuántos labs del pool <c>chequeo=true</c> ordena la visita de chequeo (banda inclusiva).</summary>
    public int MinLabs { get; set; } = 2;
    public int MaxLabs { get; set; } = 4;
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
    /// Prob. de citar al paciente que se refiere al hospital. Es su <b>control post-alta</b>: la visita en
    /// la que la clínica retoma el seguimiento cuando le dan de alta. Def. 0,95 (casi siempre).
    /// </summary>
    public double FollowUpReferido { get; set; } = 0.95;
    // El antiguo LabResult (fracción de resultados que "volvían" el mismo día) lo sustituye
    // Simulation.Laboratorio: ahora el "cuándo" lo decide el catálogo (se hace en la clínica → hoy;
    // externo → dias_entrega_*) y el "si llega" es Laboratorio.ProbResultadoLlega.
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
