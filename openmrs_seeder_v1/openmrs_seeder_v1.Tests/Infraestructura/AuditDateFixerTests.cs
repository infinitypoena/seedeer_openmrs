using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests.Infraestructura;

/// <summary>
/// La corrección de fechas vive en los stored procedures (se verifica contra la BD real). Lo que sí se
/// puede fijar aquí es el contrato del desfase de registro, que el SP replica: si uno de los dos cambia
/// sin el otro, el paciente quedaría registrado en un instante distinto del que calcula la app.
/// </summary>
public class AuditDateFixerTests
{
    [Fact]
    public void DesfaseRegistro_SiempreEntre5Y20Minutos()
    {
        // El registro precede a la primera visita (recepción → consulta): nunca 0 (registro simultáneo
        // a la visita) ni tan grande que se salga del horario de apertura.
        for (var patientId = 1; patientId <= 5000; patientId++)
        {
            var desfase = AuditDateFixer.DesfaseRegistroMinutos(patientId);
            Assert.InRange(desfase, 5, 20);
        }
    }

    [Fact]
    public void DesfaseRegistro_EsDeterministaPorPaciente()
    {
        // Re-ejecutar el proceso no debe mover la fecha de registro de nadie (idempotencia)
        Assert.Equal(AuditDateFixer.DesfaseRegistroMinutos(1234), AuditDateFixer.DesfaseRegistroMinutos(1234));
    }

    [Fact]
    public void DesfaseRegistro_VariaEntrePacientes()
    {
        // Si todos entraran con el mismo desfase, las altas del día formarían una fila artificial
        var distintos = Enumerable.Range(1, 100)
            .Select(AuditDateFixer.DesfaseRegistroMinutos)
            .Distinct()
            .Count();

        Assert.True(distintos > 8, $"Se esperaba variedad de desfases, hubo {distintos}");
    }

    [Fact]
    public void FormatearLineaLog_SinTabla_SoloFaseYMensaje()
    {
        Assert.Equal("[aplicar] inicio (lote 5000)",
            AuditDateFixer.FormatearLineaLog("aplicar", null, null, "inicio (lote 5000)"));
    }

    [Fact]
    public void FormatearLineaLog_ConTabla_ColumnasAlineadas()
    {
        // El mismo formato lo usan el volcado normal y el poller de progreso en vivo: si divergen,
        // el log de la etapa 5/5 se vuelve ilegible a mitad del CALL largo
        Assert.Equal("[aplicar] obs                                5000  lote pk [0, 5000)",
            AuditDateFixer.FormatearLineaLog("aplicar", "obs", 5000, "lote pk [0, 5000)"));
    }
}
