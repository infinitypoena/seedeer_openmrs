using OpenmrsSeeder.Configuration;
using OpenmrsSeeder.Models.Catalogs;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Boost diario de un departamento NO principal en la selección de direcciones. Seam puro y
/// determinista: dado un seed y una fecha devuelve (a lo sumo) un departamento a potenciar y la cuota
/// del volumen del día que captará (fracción 0–1). No consume el RNG compartido del generador de
/// perfiles (construye el suyo propio sembrado con <c>randomSeed + RandomSeedOffset + fecha.DayNumber</c>),
/// de modo que la elección es reproducible y no desplaza el flujo aleatorio del resto de la corrida.
/// </summary>
public static class DireccionBoost
{
    /// <summary>Offset de RNG dedicado a esta feature (libre: la app usa +1..+24; +30 es del harness).</summary>
    public const int RandomSeedOffset = 25;

    /// <summary>
    /// Departamento elegido para potenciar el día <paramref name="fecha"/> y su cuota objetivo, o
    /// <c>(null, 0.0)</c> si ese día no hay boost. Con la feature apagada (o sin direcciones) devuelve
    /// <c>(null, 0.0)</c> SIN consumir RNG, para que el flujo histórico quede bit a bit intacto.
    /// </summary>
    public static (string? departamento, double share) ElegirDepartamentoBoost(
        IReadOnlyList<DireccionEntry> direcciones,
        DireccionesSettings cfg,
        int randomSeed,
        DateOnly fecha)
    {
        if (!cfg.Enabled || direcciones.Count == 0)
            return (null, 0.0);

        // Peso total por departamento (case-insensitive: el CSV es coherente, pero no confiar).
        var porDepartamento = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in direcciones)
        {
            porDepartamento.TryGetValue(d.Departamento, out var acumulado);
            porDepartamento[d.Departamento] = acumulado + d.Peso;
        }

        // El dominante (mayor peso total) es "la principal": se excluye del boost.
        var dominante = porDepartamento
            .OrderByDescending(kv => kv.Value)
            .First().Key;

        var elegibles = porDepartamento
            .Where(kv => kv.Value > 0 && !string.Equals(kv.Key, dominante, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        if (elegibles.Count == 0)
            return (null, 0.0);

        var rng = new Random(unchecked(randomSeed + RandomSeedOffset + fecha.DayNumber));

        if (rng.NextDouble() >= cfg.ProbabilidadBoostDia)
            return (null, 0.0);

        var elegido = elegibles[rng.Next(elegibles.Count)];
        return (elegido, cfg.Share);
    }

    /// <summary>
    /// Multiplicador de peso de las filas del departamento potenciado para que capture exactamente la
    /// cuota <paramref name="share"/> del día: <c>share = (P·m) / (P·m + resto)</c>. Así un interior de
    /// peso 2 y otro de 60 alcanzan la MISMA cuota (el factor se recalcula según su peso relativo).
    /// Devuelve 1.0 si no hay boost (departamento nulo o cuota fuera de (0,1)) o si no hay a quién
    /// reescalar (solo existe el departamento potenciado o su peso es 0).
    /// </summary>
    public static double MultiplicadorBoost(
        IReadOnlyList<DireccionEntry> direcciones,
        string? departamento,
        double share)
    {
        if (string.IsNullOrEmpty(departamento) || share <= 0 || share >= 1)
            return 1.0;

        double pesoBoost = 0, pesoOtros = 0;
        foreach (var d in direcciones)
        {
            if (string.Equals(d.Departamento, departamento, StringComparison.OrdinalIgnoreCase))
                pesoBoost += d.Peso;
            else
                pesoOtros += d.Peso;
        }

        if (pesoBoost <= 0 || pesoOtros <= 0)
            return 1.0;

        // Despejando share = (pesoBoost·m) / (pesoBoost·m + pesoOtros)
        return share * pesoOtros / (pesoBoost * (1 - share));
    }
}
