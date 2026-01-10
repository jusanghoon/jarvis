using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace javis.Services.MainAi;

public sealed class MainAiReleaseNotesStore
{
    private readonly string _path;

    public MainAiReleaseNotesStore(string dataDir)
    {
        var dir = Path.Combine(dataDir, "main_ai");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "release_notes.jsonl");
    }

    public void Append(string title, string body, string? tag = null, string? source = null)
    {
        title = (title ?? "").Trim();
        body = (body ?? "").Trim();
        if (title.Length == 0 && body.Length == 0) return;

        var rec = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.Now.ToString("O"),
            ["id"] = "rn_" + Guid.NewGuid().ToString("N"),
            ["kind"] = "release_note",
            ["title"] = title,
            ["body"] = body,
            ["tag"] = tag,
            ["source"] = source
        };

        var line = JsonSerializer.Serialize(rec) + "\n";
        File.AppendAllText(_path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public IReadOnlyList<ReleaseNoteItem> ReadLatest(int take = 20)
    {
        take = Math.Clamp(take, 1, 200);
        if (!File.Exists(_path)) return Array.Empty<ReleaseNoteItem>();

        var list = new List<ReleaseNoteItem>();
        foreach (var line in File.ReadLines(_path).Reverse())
        {
            if (list.Count >= take) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var kind = root.TryGetProperty("kind", out var kEl) ? (kEl.GetString() ?? "") : "";
                if (!string.Equals(kind, "release_note", StringComparison.OrdinalIgnoreCase)) continue;

                var ts = root.TryGetProperty("ts", out var tsEl) ? (tsEl.GetString() ?? "") : "";
                var title = root.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "") : "";
                var body = root.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";
                var tag = root.TryGetProperty("tag", out var tagEl) ? (tagEl.GetString() ?? "") : "";

                list.Add(new ReleaseNoteItem
                {
                    Ts = ts,
                    Title = title,
                    Body = body,
                    Tag = string.IsNullOrWhiteSpace(tag) ? null : tag
                });
            }
            catch { }
        }

        list.Reverse();
        return list;
    }

    public sealed class ReleaseNoteItem
    {
        public string Ts { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string? Tag { get; set; }
    }
}
