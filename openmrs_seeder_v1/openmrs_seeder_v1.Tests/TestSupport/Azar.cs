namespace openmrs_seeder_v1.Tests.TestSupport;

/// <summary>
/// Azar determinista para toda la suite. Regla de la casa: <b>ningún test crea un Random sin semilla</b> —
/// un test estadístico sin semilla fija es una moneda al aire con nombre de test.
/// </summary>
public static class Azar
{
    /// <summary>RNG con semilla fija (42 salvo que el test necesite explorar otra).</summary>
    public static Random Rng(int seed = 42) => new(seed);

    /// <summary>
    /// Cola de "tiradas" prefijadas para seams que reciben <c>Func&lt;double&gt;</c>: devuelve los valores
    /// en orden y, agotados, repite el último (así el test no revienta si el seam tira una vez de más).
    /// </summary>
    public static Func<double> Rolls(params double[] valores)
    {
        if (valores.Length == 0) throw new ArgumentException("Hace falta al menos un roll.", nameof(valores));
        var i = 0;
        return () => valores[Math.Min(i++, valores.Length - 1)];
    }
}
