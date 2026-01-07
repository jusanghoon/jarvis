using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services;

public sealed class KnowledgeCanon
{
    public string Dir { get; }
    public string CanonPath { get; }

    private readonly JsonSerializerOptions _opt = new() { WriteIndented = false };

    public KnowledgeCanon(string dataDir)
    {
        Dir = Path.Combine(dataDir, "knowledge");
        Directory.CreateDirectory(Dir);
        CanonPath = Path.Combine(Dir, "canon.jsonl");
    }

    public async Task AppendAsync(string title, string body, string[]? tags = null, string kind = "canon", CancellationToken ct = default)
    {
        title = (title ?? "").Trim();
        body = (body ?? "").Trim();
        if (title.Length == 0 || body.Length == 0) return;

        var id = "k_" + Sha1Hex(title + "\n" + body).Substring(0, 12);

        // 중복 방지(가벼운 방식): 파일에 동일 id 있으면 스킵
        if (Exists(id)) return;

        var item = new CanonItem(
            id: id,
            ts: DateTimeOffset.Now.ToString("O"),
            kind: kind,
            title: title,
            body: body,
            tags: tags ?? Array.Empty<string>()
        );

        var line = JsonSerializer.Serialize(item, _opt) + "\n";
        await File.AppendAllTextAsync(CanonPath, line, Encoding.UTF8, ct);
    }

    public string BuildPromptBlock(string query, int maxItems = 6)
    {
        var items = Retrieve(query, maxItems);
        if (items.Count == 0) return "(캐논 지식 없음)";

        var sb = new StringBuilder();
        sb.AppendLine("자비스 캐논 지식(정본):");
        foreach (var it in items)
        {
            sb.AppendLine($"- [{it.id}] {it.title}");
            sb.AppendLine($"  {TrimMax(it.body, 700)}");
            if (it.tags.Length > 0) sb.AppendLine($"  tags: {string.Join(", ", it.tags.Take(8))}");
        }
        return sb.ToString();
    }

    public List<CanonItem> Retrieve(string query, int maxItems)
    {
        if (!File.Exists(CanonPath)) return new();

        var q = (query ?? "").Trim().ToLowerInvariant();
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Select(t => t.Trim())
                      .Where(t => t.Length >= 2)
                      .Take(10)
                      .ToArray();

        var all = new List<CanonItem>();

        foreach (var line in File.ReadLines(CanonPath))
        {
            try
            {
                var it = JsonSerializer.Deserialize<CanonItem>(line);
                if (it is null) continue;
                all.Add(it);
            }
            catch { }
        }

        if (all.Count == 0) return new();

        // query 없으면 최근 것 위주
        if (tokens.Length == 0)
            return all.TakeLast(maxItems).ToList();

        // 간단 스코어링 검색
        var scored = all.Select(it => (it, score: Score(it, tokens)))
                        .Where(x => x.score > 0)
                        .OrderByDescending(x => x.score)
                        .Take(maxItems)
                        .Select(x => x.it)
                        .ToList();

        // 부족하면 최근으로 채우기
        if (scored.Count < maxItems)
        {
            foreach (var it in all.AsEnumerable().Reverse())
            {
                if (scored.Any(x => x.id == it.id)) continue;
                scored.Add(it);
                if (scored.Count >= maxItems) break;
            }
        }

        return scored;
    }

    private bool Exists(string id)
    {
        if (!File.Exists(CanonPath)) return false;
        foreach (var line in File.ReadLines(CanonPath))
            if (line.Contains($"\"id\":\"{id}\"", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static int Score(CanonItem it, string[] tokens)
    {
        var text = (it.title + " " + it.body + " " + string.Join(' ', it.tags)).ToLowerInvariant();
        int s = 0;
        foreach (var t in tokens)
            if (text.Contains(t)) s += 2;
        return s;
    }

    private static string Sha1Hex(string s)
    {
        using var sha1 = SHA1.Create();
        var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string TrimMax(string s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}

public sealed record CanonItem(
    string id,
    string ts,
    string kind,
    string title,
    string body,
    string[] tags
);
