namespace OpenmrsSeeder.Services;

/// <summary>
/// Genera identificadores válidos para el tipo "OpenMRS ID" usando el algoritmo
/// Luhn Mod-30 con el juego de caracteres estándar del módulo idgen de OpenMRS.
/// El counter se inicializa de forma aleatoria entre 400M y 700M (el máximo para
/// 6 chars es 30^6 = 729M; idgen de la instancia está en ~24.3M).
/// El offset aleatorio evita colisiones entre ejecuciones del seeder.
/// </summary>
public static class LuhnMod30Generator
{
    private const string CharSet = "0123456789ACDEFGHJKLMNPRTUVWXY";
    private const int BaseLen = 6;
    private static long _counter = Random.Shared.NextInt64(400_000_000L, 700_000_000L);

    public static string Next()
    {
        var seq   = Interlocked.Increment(ref _counter);
        var base30 = ToBase30(seq, BaseLen);
        return base30 + ComputeCheckDigit(base30);
    }

    private static string ToBase30(long num, int length)
    {
        var chars = new char[length];
        for (int i = length - 1; i >= 0; i--)
        {
            chars[i] = CharSet[(int)(num % 30)];
            num /= 30;
        }
        return new string(chars);
    }

    private static char ComputeCheckDigit(string identifier)
    {
        int total      = 0;
        bool alternate = true; // el char más a la derecha de la base queda en posición 1 del ID completo → se dobla
        for (int i = identifier.Length - 1; i >= 0; i--)
        {
            int d = CharSet.IndexOf(identifier[i]);
            if (alternate)
            {
                d *= 2;
                if (d > 29) d -= 29;
            }
            total    += d;
            alternate = !alternate;
        }
        return CharSet[(30 - total % 30) % 30];
    }
}
