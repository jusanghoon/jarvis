using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace javis.Services;

public sealed class OllamaChatService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public OllamaChatService(string baseUrl = "http://localhost:11434/api")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient();
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string model,
        List<OllamaMessage> messages,
        bool think,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"{_baseUrl}/chat";

        var req = new OllamaChatRequest
        {
            Model = model,
            Messages = messages,
            Stream = true,
            Think = think ? (object)true : null
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(req, JsonOpts), Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line, JsonOpts);
            if (chunk?.Message?.Content is { Length: > 0 } delta)
                yield return delta;

            if (chunk?.Done == true)
                yield break;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ---- DTOs ----
    public sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; set; } = [];
        [JsonPropertyName("stream")] public bool Stream { get; set; } = true;
        [JsonPropertyName("think")] public object? Think { get; set; } = null;
    }

    public sealed class OllamaChatChunk
    {
        [JsonPropertyName("message")] public OllamaMessage? Message { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
    }

    public sealed class OllamaMessage
    {
        public OllamaMessage() { }

        public OllamaMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }
}
