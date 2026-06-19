using System.Text;
using System.Text.Json;

namespace OpenmrsSeeder.Clients;

public class OpenMrsRestClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OpenMrsRestClient(HttpClient http) => _http = http;

    public async Task<string> GetAsync(string path, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
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
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(path, content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        var json = await PostAsync(path, body, ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(path, ct);
        response.EnsureSuccessStatusCode();
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
}
