using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class OrderVigenciaTests
{
    private const string Hba1c = "hba1c-uuid";

    [Fact]
    public void SinRegistro_NoEstaActivo()
    {
        var vigencias = new Dictionary<string, DateOnly>();
        Assert.False(OrderVigencia.EstaActivo(vigencias, Hba1c, new DateOnly(2024, 6, 1)));
    }

    [Fact]
    public void OrdenVigente_EstaActivo_HastaSuFechaInclusive()
    {
        var vigencias = new Dictionary<string, DateOnly> { [Hba1c] = new DateOnly(2024, 6, 8) };

        // Antes de la fecha de vigencia: activa.
        Assert.True(OrderVigencia.EstaActivo(vigencias, Hba1c, new DateOnly(2024, 6, 1)));
        // El día exacto de vigencia: aún activa (comparación >=).
        Assert.True(OrderVigencia.EstaActivo(vigencias, Hba1c, new DateOnly(2024, 6, 8)));
    }

    [Fact]
    public void OrdenExpirada_NoEstaActivo_ControlCronicoPuedeReordenar()
    {
        var vigencias = new Dictionary<string, DateOnly> { [Hba1c] = new DateOnly(2024, 6, 8) };
        // El control crónico cae 30+ días después: la orden ya expiró → se puede volver a pedir.
        Assert.False(OrderVigencia.EstaActivo(vigencias, Hba1c, new DateOnly(2024, 7, 10)));
    }

    [Fact]
    public void OtroConcepto_NoAfectado()
    {
        var vigencias = new Dictionary<string, DateOnly> { [Hba1c] = new DateOnly(2024, 6, 8) };
        Assert.False(OrderVigencia.EstaActivo(vigencias, "glucosa-uuid", new DateOnly(2024, 6, 1)));
    }
}
