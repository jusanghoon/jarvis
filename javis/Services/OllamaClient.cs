using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
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
        var requestUrl = $"{_baseUrl.TrimEnd('/')}/api/generate";
        Debug.WriteLine($"[Ollama] Final URL: {requestUrl}");

        using var req = new HttpRequestMessage(HttpMethod.Post, requestUrl)
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

    public async IAsyncEnumerable<string> StreamGenerateAsync(string prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        var requestUrl = $"{_baseUrl.TrimEnd('/')}/api/generate";

        using var req = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { model = _model, prompt, stream = true }),
                Encoding.UTF8,
                "application/json")
        };

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("response", out var rEl))
            {
                var s = rEl.GetString() ?? "";
                if (s.Length > 0)
                    yield return s;
            }

            if (root.TryGetProperty("done", out var dEl) && dEl.ValueKind == JsonValueKind.True)
                yield break;
        }
    }
}
