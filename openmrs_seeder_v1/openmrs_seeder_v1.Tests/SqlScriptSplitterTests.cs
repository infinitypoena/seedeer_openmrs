using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests;

/// <summary>
/// El .sql de los stored procedures tiene que servir para dos clientes: el CLI de MariaDB (que entiende
/// DELIMITER) y el driver de .NET (que no). El splitter es lo que reconcilia ambos.
/// </summary>
public class SqlScriptSplitterTests
{
    [Fact]
    public void SeparaSentenciasSimplesPorPuntoYComa()
    {
        var sentencias = SqlScriptSplitter.Split("SELECT 1; SELECT 2;");

        Assert.Equal(["SELECT 1", "SELECT 2"], sentencias);
    }

    [Fact]
    public void ElCuerpoDeUnProcedimientoNoSePartePorSusPuntoYComa()
    {
        // Sin honrar DELIMITER, los ';' internos partirían el CREATE PROCEDURE en trozos inválidos
        var script = """
            CREATE TABLE t (id INT);
            DELIMITER $$
            CREATE PROCEDURE p()
            BEGIN
                SET @x = 1;
                SELECT @x;
            END $$
            DELIMITER ;
            SELECT 'fin';
            """;

        var sentencias = SqlScriptSplitter.Split(script);

        Assert.Equal(3, sentencias.Count);
        Assert.StartsWith("CREATE TABLE", sentencias[0]);
        Assert.StartsWith("CREATE PROCEDURE", sentencias[1]);
        Assert.Contains("SET @x = 1;", sentencias[1]);   // el ';' interno sobrevive dentro del cuerpo
        Assert.Contains("END", sentencias[1]);
        Assert.Equal("SELECT 'fin'", sentencias[2]);
    }

    [Fact]
    public void IgnoraLosPuntoYComaDeLosComentarios()
    {
        // El encabezado del script real documenta los CALL, que llevan ';': no deben partir nada
        var script = """
            -- Uso: CALL sp_x('SIM-', 3); CALL sp_y();
            SELECT 1;
            """;

        var sentencia = Assert.Single(SqlScriptSplitter.Split(script));
        Assert.Equal("SELECT 1", sentencia);
    }

    [Fact]
    public void ElDelimitadorDentroDeUnLiteralNoCorta()
    {
        var sentencia = Assert.Single(SqlScriptSplitter.Split("SELECT 'a;b';"));

        Assert.Equal("SELECT 'a;b'", sentencia);
    }

    [Fact]
    public void ElScriptRealDelRepoSeTroceaEnSentenciasEjecutables()
    {
        // Red contra ediciones futuras del .sql: ninguna sentencia puede quedar con un DELIMITER dentro
        // (el driver lo rechazaría) y los 4 procedimientos deben salir enteros.
        var script = File.ReadAllText(RutaDelScript());

        var sentencias = SqlScriptSplitter.Split(script);

        Assert.DoesNotContain(sentencias, s => s.Contains("DELIMITER", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, sentencias.Count(s => s.StartsWith("CREATE PROCEDURE", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(sentencias, s => s.StartsWith("CREATE TABLE IF NOT EXISTS sim_fecha_log"));
    }

    /// <summary>Sube desde el binario de los tests hasta la raíz del repo para encontrar querys/.</summary>
    private static string RutaDelScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidato = Path.Combine(dir.FullName, "querys", "sp_fechas_auditoria.sql");
            if (File.Exists(candidato)) return candidato;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("No se encontró querys/sp_fechas_auditoria.sql subiendo desde " + AppContext.BaseDirectory);
    }
}
