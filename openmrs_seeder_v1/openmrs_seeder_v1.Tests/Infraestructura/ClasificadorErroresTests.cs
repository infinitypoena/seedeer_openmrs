using System.Net;
using OpenmrsSeeder.Services;

namespace openmrs_seeder_v1.Tests.Infraestructura;

/// <summary>
/// El clasificador es lo que separa "un dato que OpenMRS rechazó y se repetirá" (4xx) de "el entorno
/// falló y otra corrida pasaría" (5xx/timeout/red). Depende de que la excepción llegue tipada desde
/// el cliente REST (HttpRequestException con StatusCode) — estos tests fijan ese contrato.
/// </summary>
public class ClasificadorErroresTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void Http4xx_EsDatoRechazado(HttpStatusCode status)
    {
        var ex = new HttpRequestException("rechazado", null, status);
        Assert.Equal(TipoError.DatoRechazado, ClasificadorErrores.Clasificar(ex));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Http5xx_EsServidor(HttpStatusCode status)
    {
        var ex = new HttpRequestException("caído", null, status);
        Assert.Equal(TipoError.Servidor, ClasificadorErrores.Clasificar(ex));
    }

    [Fact]
    public void HttpSinStatus_EsRed()
    {
        // Conexión rechazada, DNS, socket: HttpRequestException sin respuesta HTTP
        var ex = new HttpRequestException("connection refused");
        Assert.Equal(TipoError.Red, ClasificadorErrores.Clasificar(ex));
    }

    [Fact]
    public void TaskCanceledConTimeoutDentro_EsTimeout()
    {
        // Así reporta HttpClient su timeout de 30 s — distinto del Ctrl+C
        var ex = new TaskCanceledException("timed out", new TimeoutException());
        Assert.Equal(TipoError.Timeout, ClasificadorErrores.Clasificar(ex));
    }

    [Fact]
    public void CancelacionPorToken_EsCancelado()
    {
        Assert.Equal(TipoError.Cancelado, ClasificadorErrores.Clasificar(new OperationCanceledException()));
        // TaskCanceledException SIN TimeoutException dentro también es cancelación, no timeout
        Assert.Equal(TipoError.Cancelado, ClasificadorErrores.Clasificar(new TaskCanceledException()));
    }

    [Fact]
    public void ExcepcionNoHttp_YAusencia_SonOtro()
    {
        Assert.Equal(TipoError.Otro, ClasificadorErrores.Clasificar(new InvalidOperationException("x")));
        // Sin excepción (un LogError que no la pasó): honesto y visible, no un fallo del clasificador
        Assert.Equal(TipoError.Otro, ClasificadorErrores.Clasificar(null));
    }

    [Fact]
    public void EtiquetaYCodigo_CubrenTodoElEnum()
    {
        foreach (var tipo in Enum.GetValues<TipoError>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ClasificadorErrores.Etiqueta(tipo)));
            Assert.False(string.IsNullOrWhiteSpace(ClasificadorErrores.Codigo(tipo)));
        }
        // Los códigos del CSV son estables (los consume quien filtre el fichero)
        Assert.Equal("4xx", ClasificadorErrores.Codigo(TipoError.DatoRechazado));
        Assert.Equal("5xx", ClasificadorErrores.Codigo(TipoError.Servidor));
        Assert.Equal("timeout", ClasificadorErrores.Codigo(TipoError.Timeout));
    }
}
