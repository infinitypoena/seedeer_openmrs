using System.Text;

namespace OpenmrsSeeder.Services;

/// <summary>
/// Trocea un script .sql en sentencias ejecutables una a una.
///
/// Hace falta porque <c>DELIMITER</c> es una directiva del CLIENTE de MariaDB, no SQL: el driver la
/// rechaza. Pero sin ella no se puede definir un stored procedure, cuyo cuerpo lleva <c>;</c> dentro.
/// Este splitter interpreta la directiva igual que el CLI (cambia el separador de sentencias), de modo
/// que el MISMO fichero sirve para las dos vías: <c>mariadb &lt; script.sql</c> y la app.
///
/// Respeta literales de cadena y comentarios de línea (un <c>;</c> dentro de un comentario del
/// encabezado no debe partir nada). Puro y estático: testeable sin base de datos.
/// </summary>
public static class SqlScriptSplitter
{
    public static IReadOnlyList<string> Split(string script)
    {
        var sentencias  = new List<string>();
        var sb          = new StringBuilder();
        var delimitador = ";";
        var enInicioLinea = true;
        var i = 0;

        while (i < script.Length)
        {
            var c = script[i];

            // Directiva DELIMITER (solo al principio de línea, como en el CLI): cambia el separador
            if (enInicioLinea && Coincide(script, i, "DELIMITER"))
            {
                var finLinea = FinDeLinea(script, i);
                var nuevo = script[(i + "DELIMITER".Length)..finLinea].Trim();
                if (nuevo.Length > 0) delimitador = nuevo;
                i = finLinea;
                continue;
            }

            // Comentario de línea: se descarta (puede contener ';' o el delimitador)
            if (c == '-' && i + 1 < script.Length && script[i + 1] == '-')
            {
                i = FinDeLinea(script, i);
                enInicioLinea = false;
                continue;
            }

            // Literal: se copia entero; lo de dentro nunca es delimitador
            if (c is '\'' or '"' or '`')
            {
                i = CopiarLiteral(script, i, sb);
                enInicioLinea = false;
                continue;
            }

            if (Coincide(script, i, delimitador))
            {
                Cerrar(sentencias, sb);
                i += delimitador.Length;
                enInicioLinea = false;
                continue;
            }

            sb.Append(c);
            enInicioLinea = c == '\n' || (enInicioLinea && char.IsWhiteSpace(c));
            i++;
        }

        Cerrar(sentencias, sb);
        return sentencias;
    }

    private static int FinDeLinea(string s, int desde)
    {
        var fin = s.IndexOf('\n', desde);
        return fin < 0 ? s.Length : fin;
    }

    /// <summary>Copia el literal que empieza en <paramref name="i"/> y devuelve el índice tras su cierre.</summary>
    private static int CopiarLiteral(string s, int i, StringBuilder sb)
    {
        var cierre = s[i];
        sb.Append(s[i++]);
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                sb.Append(s[i]).Append(s[i + 1]);
                i += 2;
                continue;
            }
            sb.Append(s[i]);
            if (s[i] == cierre) return i + 1;
            i++;
        }
        return i;
    }

    private static bool Coincide(string s, int i, string texto) =>
        texto.Length > 0
        && i + texto.Length <= s.Length
        && string.CompareOrdinal(s, i, texto, 0, texto.Length) == 0;

    private static void Cerrar(List<string> sentencias, StringBuilder sb)
    {
        var sentencia = sb.ToString().Trim();
        if (sentencia.Length > 0) sentencias.Add(sentencia);
        sb.Clear();
    }
}
