using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services;

public sealed class OllamaClient
{
    private readonly HttpClient _http = new();
    private readonly string _baseUrl;
    private readonly string _model;

    public OllamaClient(string baseUrl, string model)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    public Task<string> GenerateAsync(string prompt)
        => GenerateAsync(prompt, CancellationToken.None);

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { model = _model, prompt, stream = false }),
                Encoding.UTF8,
                "application/json")
        };

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("response").GetString() ?? "";
    }
}
