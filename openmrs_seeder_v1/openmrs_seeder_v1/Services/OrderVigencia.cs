namespace OpenmrsSeeder.Services;

/// <summary>
/// Vigencia de órdenes (labs/fármacos) por fecha. Sustituye el bloqueo permanente de re-ordenar un
/// concepto: una orden solo impide volver a pedir el MISMO orderable mientras siga activa (hasta su
/// fecha de vigencia — <c>autoExpireDate</c> del lab o <c>dateActivated + duración</c> del fármaco).
/// Pasada esa fecha, un control crónico puede volver a ordenar la misma HbA1c / metformina sin caer en
/// el <c>AmbiguousOrderException</c> de OpenMRS (que solo rechaza dos órdenes ACTIVAS del mismo orderable).
/// Seam puro y testeable.
/// </summary>
public static class OrderVigencia
{
    /// <summary>
    /// True si el concepto tiene una orden aún activa (vigente) a <paramref name="fechaVisita"/>:
    /// existe un registro y su fecha de vigencia es &gt;= la fecha de la visita. Sin registro = no activo.
    /// </summary>
    public static bool EstaActivo(
        IReadOnlyDictionary<string, DateOnly> vigencias, string conceptUuid, DateOnly fechaVisita) =>
        vigencias.TryGetValue(conceptUuid, out var vigenteHasta) && vigenteHasta >= fechaVisita;
}
