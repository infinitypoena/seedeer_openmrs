using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenmrsSeeder.Clients;

public class OpenMrsRestClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenMrsRestClient>? _logger;

    /// <summary>Esperas entre reintentos ante fallos transitorios (2 reintentos máx.).</summary>
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(900)];

    /// <summary>Los errores de OpenMRS traen el stacktrace Java completo — al log va solo el inicio.</summary>
    private const int MaxErrorBody = 400;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OpenMrsRestClient(HttpClient http, ILogger<OpenMrsRestClient>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> GetAsync(string path, CancellationToken ct = default)
    {
        var response = await SendConReintentosAsync(
            token => _http.GetAsync(path, token), $"GET {path}", esPost: false, ct);
        await LanzarSiFallaAsync(response, $"GET {path}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var json = await GetAsync(path, ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    public async Task<string> PostAsync(string path, object body, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        // El contenido se crea por intento (un StringContent no puede reenviarse tras un send).
        var response = await SendConReintentosAsync(
            token => _http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"), token),
            $"POST {path}", esPost: true, ct);
        await LanzarSiFallaAsync(response, $"POST {path}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        var json = await PostAsync(path, body, ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var response = await SendConReintentosAsync(
            token => _http.DeleteAsync(path, token), $"DELETE {path}", esPost: false, ct);
        await LanzarSiFallaAsync(response, $"DELETE {path}", ct);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("session", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Envía con hasta 2 reintentos ante fallos TRANSITORIOS (los 500 por contención se observaron
    /// en vivo). En GET/DELETE se reintenta todo 5xx y los fallos de red/timeout (idempotentes);
    /// en POST solo 502/503/504 — el gateway no llegó a procesar nada, reintentar es seguro; un 500
    /// de OpenMRS pudo haber aplicado el insert y reintentar duplicaría datos.
    /// </summary>
    private async Task<HttpResponseMessage> SendConReintentosAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, string descriptor, bool esPost, CancellationToken ct)
    {
        for (int intento = 0; ; intento++)
        {
            try
            {
                var response = await send(ct);
                if (response.IsSuccessStatusCode ||
                    intento >= Backoff.Length ||
                    !EsTransitorio(response.StatusCode, esPost))
                    return response;

                _logger?.LogWarning("[Rest] {Desc} → {Code}; reintento {N}/{Max} en {Delay} ms",
                    descriptor, (int)response.StatusCode, intento + 1, Backoff.Length,
                    Backoff[intento].TotalMilliseconds);
                response.Dispose();
            }
            catch (Exception ex) when (
                !ct.IsCancellationRequested && !esPost && intento < Backoff.Length &&
                ex is HttpRequestException or TaskCanceledException)
            {
                // Fallo de red/timeout en operación idempotente → reintentar
                _logger?.LogWarning("[Rest] {Desc} falló ({Msg}); reintento {N}/{Max}",
                    descriptor, ex.Message, intento + 1, Backoff.Length);
            }

            await Task.Delay(Backoff[intento], ct);
        }
    }

    private static bool EsTransitorio(System.Net.HttpStatusCode code, bool esPost) =>
        esPost
            ? code is System.Net.HttpStatusCode.BadGateway
                   or System.Net.HttpStatusCode.ServiceUnavailable
                   or System.Net.HttpStatusCode.GatewayTimeout
            : (int)code >= 500;

    private static async Task LanzarSiFallaAsync(HttpResponseMessage response, string descriptor, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var errorBody = await response.Content.ReadAsStringAsync(ct);
        if (errorBody.Length > MaxErrorBody) errorBody = errorBody[..MaxErrorBody] + "…";
        throw new HttpRequestException(
            $"{descriptor} → {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}",
            null, response.StatusCode);
    }
}
