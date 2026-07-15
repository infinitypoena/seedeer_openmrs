namespace OpenmrsSeeder.Models.Simulation;

/// <summary>
/// Un día del plan precalculado. <b>Solo dice cuántas ALTAS habrá</b> — y solo se usa con el crecimiento
/// apagado (con él activo, las altas las pide la difusión de Bass a partir del estado real del pool).
///
/// <para>⚠️ Los <b>recurrentes no se pueden planificar</b>: son la demanda del panel (quién tiene cita hoy,
/// quién vuelve por su cuenta) y depende de lo que haya pasado hasta hoy. El campo
/// <c>PacientesRecurrentes</c> que había aquí venía de repartir el total con un porcentaje fijo, y era el
/// origen del cupo que estranguló la agenda. Ver <c>leyes_simulacion.md</c>.</para>
/// </summary>
public class DailySchedule
{
    public DateOnly Date { get; set; }
    /// <summary>Volumen del día en el plan estático. Con los recurrentes fuera del plan, coincide con <see cref="NuevosPacientes"/>.</summary>
    public int TotalPatients { get; set; }
    /// <summary>Altas del día (pacientes nuevos).</summary>
    public int NuevosPacientes { get; set; }
}
