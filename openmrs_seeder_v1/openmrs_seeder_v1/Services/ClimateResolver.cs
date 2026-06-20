using System.Globalization;
using OpenmrsSeeder.Configuration;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Resuelve la estación y la temperatura ambiente para una fecha, a partir del catálogo
/// opcional clima.csv (por semana ISO). Si el clima está deshabilitado o no hay datos para
/// la semana, devuelve (null, null) → la simulación se comporta de forma neutra.
/// </summary>
public class ClimateResolver
{
    private readonly bool _enabled;
    private readonly Dictionary<int, (string estacion, double tempC)> _porSemana;

    public ClimateResolver(CatalogLoader catalogs, SimulationSettings settings)
    {
        _enabled = settings.Climate.Enabled;
        _porSemana = catalogs.Clima
            .GroupBy(c => c.Semana)
            .ToDictionary(g => g.Key, g => (g.Last().Estacion, g.Last().TempPromedioC));
    }

    /// <summary>True si hay un catálogo de clima activo con datos.</summary>
    public bool Activo => _enabled && _porSemana.Count > 0;

    public (string? estacion, double? tempC) Resolve(DateOnly date)
    {
        if (!Activo) return (null, null);

        var semana = ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
        if (_porSemana.TryGetValue(semana, out var v) && !string.IsNullOrEmpty(v.estacion))
            return (v.estacion, v.tempC);

        return (null, null);
    }
}
