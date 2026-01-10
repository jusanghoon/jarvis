using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using javis.Services;

namespace javis.Services.MainAi;

public sealed class MainAiDocSuggestionStore
{
    private readonly string _path;

    public MainAiDocSuggestionStore(string dataDir)
    {
        var dir = Path.Combine(dataDir, "main_ai");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "doc_suggestions.jsonl");
    }

    public void AppendSuggestion(string text, string? source = null)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;

        var rec = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.Now.ToString("O"),
            ["id"] = "s_" + Guid.NewGuid().ToString("N"),
            ["kind"] = "suggestion",
            ["text"] = text,
            ["source"] = source
        };

        var line = JsonSerializer.Serialize(rec) + "\n";
        File.AppendAllText(_path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public void MarkResolved(string id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return;

        var rec = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.Now.ToString("O"),
            ["kind"] = "resolved",
            ["id"] = id
        };

        var line = JsonSerializer.Serialize(rec) + "\n";
        File.AppendAllText(_path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public IReadOnlyList<DocSuggestionItem> ReadLatestOpen(int take = 20)
    {
        take = Math.Clamp(take, 1, 200);

        if (!File.Exists(_path)) return Array.Empty<DocSuggestionItem>();

        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DocSuggestionItem>();

        foreach (var line in File.ReadLines(_path).Reverse())
        {
            if (list.Count >= take) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var kind = root.TryGetProperty("kind", out var kEl) ? (kEl.GetString() ?? "") : "";

                if (string.Equals(kind, "resolved", StringComparison.OrdinalIgnoreCase))
                {
                    var rid = root.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
                    if (!string.IsNullOrWhiteSpace(rid)) resolved.Add(rid);
                    continue;
                }

                if (!string.Equals(kind, "suggestion", StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = root.TryGetProperty("id", out var sidEl) ? (sidEl.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (resolved.Contains(id)) continue;

                var ts = root.TryGetProperty("ts", out var tsEl) ? (tsEl.GetString() ?? "") : "";
                var text = root.TryGetProperty("text", out var tEl) ? (tEl.GetString() ?? "") : "";
                var source = root.TryGetProperty("source", out var sEl) ? (sEl.GetString() ?? "") : "";

                if (string.IsNullOrWhiteSpace(text)) continue;

                list.Add(new DocSuggestionItem
                {
                    Id = id,
                    Ts = ts,
                    Text = text,
                    Source = string.IsNullOrWhiteSpace(source) ? null : source
                });
            }
            catch
            {
                // ignore bad line
            }
        }

        list.Reverse();
        return list;
    }

    public sealed class DocSuggestionItem
    {
        public string Id { get; set; } = "";
        public string Ts { get; set; } = "";
        public string Text { get; set; } = "";
        public string? Source { get; set; }
    }
}
