using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Decisiones puras del ciclo de vida de una orden de laboratorio (sin red, testeable), el equivalente
/// de <see cref="LabResultGenerator"/> para el <b>proceso</b> en vez del valor.
///
/// OpenMRS modela ese ciclo con <c>Order.fulfillerStatus</c> (<c>IN_PROGRESS</c> al tomar la muestra,
/// <c>COMPLETED</c> al validar el resultado, <c>DECLINED</c> si la muestra se rechaza), que es
/// exactamente lo que lee la cola de la app de laboratorio de O3. Sin mover ese campo, la orden se
/// queda para siempre en "Tests ordered".
/// </summary>
public static class LabWorkflow
{
    /// <summary>Estados de <c>Order.fulfillerStatus</c> que usa el simulador.</summary>
    public const string EnProceso  = "IN_PROGRESS";
    public const string Completada = "COMPLETED";
    public const string Rechazada  = "DECLINED";

    /// <summary>Qué le pasa a la muestra de esta orden.</summary>
    public enum Desenlace
    {
        /// <summary>El laboratorio rechaza la muestra (hemolizada, insuficiente, el paciente no acudió).</summary>
        MuestraRechazada,
        /// <summary>Se toma y se procesa: el resultado acaba registrándose.</summary>
        Procesada,
        /// <summary>Se toma pero el resultado nunca llega (se pierde). La orden queda en curso.</summary>
        ResultadoNuncaLlega
    }

    /// <summary>
    /// Desenlace de la orden. Las tiradas se inyectan (no se sortean aquí) para que sea puro.
    /// </summary>
    public static Desenlace DecidirDesenlace(
        double rollRechazo, double rollLlega, double probRechazo, double probResultadoLlega) =>
        rollRechazo < probRechazo      ? Desenlace.MuestraRechazada :
        rollLlega   < probResultadoLlega ? Desenlace.Procesada
                                         : Desenlace.ResultadoNuncaLlega;

    /// <summary>
    /// Fecha en que el resultado está disponible: el mismo día si la clínica procesa el examen
    /// (<c>dias_entrega_*</c> = 0), o unos días después si se refiere a un laboratorio externo.
    /// <paramref name="nextInt"/> es <c>Random.Next(minInclusive, maxExclusive)</c>.
    /// </summary>
    public static DateOnly FechaEntrega(DateOnly fechaOrden, LaboratorioEntry lab, Func<int, int, int> nextInt)
    {
        var min = Math.Max(0, lab.DiasEntregaMin);
        var max = Math.Max(min, lab.DiasEntregaMax);
        return fechaOrden.AddDays(nextInt(min, max + 1));
    }

    /// <summary>Momento de la toma de muestra: un rato después de la consulta, no a la vez.</summary>
    public static DateTime MomentoToma(DateTime fechaConsulta, int minutosDespues) =>
        fechaConsulta.AddMinutes(Math.Max(1, minutosDespues));

    public static readonly IReadOnlyList<string> MotivosRechazo =
    [
        "Muestra hemolizada",
        "Muestra insuficiente",
        "Muestra mal identificada",
        "El paciente no acudió a la toma"
    ];

    public static string MotivoRechazo(Func<int, int> nextInt) =>
        MotivosRechazo[nextInt(MotivosRechazo.Count)];

    /// <summary>Instrucción que el médico deja al laboratorio (campo <c>commentToFulfiller</c> de la orden).</summary>
    public static string ComentarioAlLaboratorio(LaboratorioEntry lab, bool solicitadoPorPaciente = false)
    {
        var instruccion = lab.SeRealizaEnClinica
            ? "Procesar en el laboratorio de la clínica"
            : "Referir a laboratorio externo";
        // Trazabilidad del chequeo voluntario: el examen no lo indicó el cuadro, lo pidió el paciente.
        return solicitadoPorPaciente ? $"Solicitado por el paciente. {instruccion}" : instruccion;
    }

    /// <summary>
    /// Nº de muestra (campo <c>accessionNumber</c>): identifica el tubo/la petición, y en los externos es
    /// la referencia con la que vuelve el resultado del laboratorio de fuera.
    /// </summary>
    public static string NumeroMuestra(DateOnly fecha, int secuencia) =>
        $"LAB-{fecha:yyyyMMdd}-{secuencia % 10000:0000}";

    /// <summary>
    /// Lo que registra el laboratorio al recoger la orden. En un estudio de imagen NO hay muestra que
    /// tomar: al paciente se le remite al centro de imágenes.
    /// </summary>
    public static string ComentarioToma(string tecnico, bool esImagen) =>
        esImagen
            ? "Paciente referido al centro de imágenes"
            : $"Muestra tomada por {tecnico}";

    public static string ComentarioValidacion(string validador, bool externo) =>
        externo
            ? $"Resultado recibido del laboratorio externo y validado por {validador}"
            : $"Resultado validado por {validador}";

    /// <summary>Cierre de un estudio externo sin valor registrable (imagen): el informe llega en papel.</summary>
    public static string ComentarioEstudioExterno() =>
        "Estudio realizado en centro externo; informe en el expediente";
}
