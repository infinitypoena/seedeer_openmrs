using System.Globalization;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Resuelve la estación y la temperatura ambiente para una fecha, a partir del catálogo
/// opcional clima.csv (por semana ISO). El interruptor es la PRESENCIA del catálogo: si hay datos, se
/// aplican; si no (o si la semana no está listada), devuelve (null, null) → la simulación se comporta de
/// forma neutra (sin boost estacional ni ajuste de temperatura). Ya no existe un flag <c>Enabled</c>.
/// </summary>
public class ClimateResolver
{
    private readonly Dictionary<int, (string estacion, double tempC)> _porSemana;

    public ClimateResolver(CatalogLoader catalogs)
    {
        _porSemana = catalogs.Clima
            .GroupBy(c => c.Semana)
            .ToDictionary(g => g.Key, g => (g.Last().Estacion, g.Last().TempPromedioC));
    }

    /// <summary>True si hay un catálogo de clima con datos (la presencia del archivo es el interruptor).</summary>
    public bool Activo => _porSemana.Count > 0;

    public (string? estacion, double? tempC) Resolve(DateOnly date)
    {
        if (!Activo) return (null, null);

        var semana = ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
        if (_porSemana.TryGetValue(semana, out var v) && !string.IsNullOrEmpty(v.estacion))
            return (v.estacion, v.tempC);

        return (null, null);
    }
}
