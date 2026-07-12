namespace OpenmrsSeeder.Models.Catalogs;

/// <summary>
/// Una fila del catálogo opcional de clima (clima.csv): por semana ISO del año,
/// la estación dominante y la temperatura ambiente promedio.
/// </summary>
public class ClimaEntry
{
    /// <summary>Semana ISO del año (1-53). Aplica cada año de la simulación.</summary>
    public int Semana { get; set; }
    /// <summary>Estación: invierno | verano | lluvia | seca.</summary>
    public string Estacion { get; set; } = "";
    /// <summary>Temperatura ambiente promedio en grados Celsius.</summary>
    public double TempPromedioC { get; set; }
}
