using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services;

public sealed class VaultIndexManager
{
    private readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };
    public string IndexDir { get; }
    public string IndexPath { get; }

    public VaultIndexManager(string dataDir)
    {
        IndexDir = Path.Combine(dataDir, "vault", "index");
        Directory.CreateDirectory(IndexDir);
        IndexPath = Path.Combine(IndexDir, "index.jsonl");
    }

    public bool IsIndexed(string sha256)
    {
        if (!File.Exists(IndexPath)) return false;

        foreach (var line in File.ReadLines(IndexPath))
        {
            if (line.Contains($"\"sha256\":\"{sha256}\"", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("\"ok\":true", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task<int> IndexNewAsync(
        IEnumerable<VaultItem> items,
        int maxFiles = 5,
        CancellationToken ct = default)
    {
        int done = 0;

        foreach (var it in items)
        {
            ct.ThrowIfCancellationRequested();
            if (done >= maxFiles) break;

            if (IsIndexed(it.sha256)) continue;

            var entry = await BuildIndexEntryAsync(it, ct);
            await AppendIndexAsync(entry, ct);
            done++;
        }

        return done;
    }

    private async Task AppendIndexAsync(VaultIndexEntry entry, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(entry, _jsonOpt) + "\n";
        await File.AppendAllTextAsync(IndexPath, line, Encoding.UTF8, ct);
    }

    private async Task<VaultIndexEntry> BuildIndexEntryAsync(VaultItem it, CancellationToken ct)
    {
        const long MAX_BYTES_TO_INDEX = 8 * 1024 * 1024;

        try
        {
            if (!File.Exists(it.storedPath))
                return VaultIndexEntry.Fail(it, "missing_file", "storedPath 파일이 없습니다.");

            if (it.sizeBytes > MAX_BYTES_TO_INDEX)
                return VaultIndexEntry.Skip(it, "too_large", $"파일이 너무 큽니다({it.sizeBytes} bytes).");

            var ext = (it.ext ?? "").ToLowerInvariant();

            if (ext is ".txt" or ".md" or ".log" or ".json" or ".csv")
            {
                var text = await ReadTextHeadAsync(it.storedPath, maxChars: 6000, ct);
                var snippet = MakeSnippet(text, 600);
                return VaultIndexEntry.Ok(it, snippet, kind: "text");
            }

            if (ext == ".docx")
            {
                var text = await ExtractDocxTextHeadAsync(it.storedPath, maxChars: 8000, ct);
                var snippet = MakeSnippet(text, 700);
                return VaultIndexEntry.Ok(it, snippet, kind: "docx-lite");
            }

            return VaultIndexEntry.Skip(it, "unsupported", $"인덱싱 미지원 확장자: {ext}");
        }
        catch (Exception ex)
        {
            return VaultIndexEntry.Fail(it, "exception", ex.Message);
        }
    }

    private static async Task<string> ReadTextHeadAsync(string path, int maxChars, CancellationToken ct)
    {
        using var fs = File.OpenRead(path);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buf = new char[maxChars];
        int n = await sr.ReadAsync(buf.AsMemory(0, maxChars), ct);
        return new string(buf, 0, n);
    }

    private static async Task<string> ExtractDocxTextHeadAsync(string path, int maxChars, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

        var entry = zip.GetEntry("word/document.xml");
        if (entry == null) return "";

        await using var es = entry.Open();
        using var sr = new StreamReader(es, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var xml = await sr.ReadToEndAsync(ct);

        var text = Regex.Replace(xml, "<[^>]+>", " ");
        text = Regex.Replace(text, "\\s+", " ").Trim();

        return text.Length <= maxChars ? text : text.Substring(0, maxChars);
    }

    private static string MakeSnippet(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text.Length <= maxChars ? text : text.Substring(0, maxChars) + "…";
    }

    public IReadOnlyList<(string fileName, string shaShort, string snippet)> ReadRecentSnippets(int maxItems = 6, int maxSnippetChars = 450, int withinDays = 14)
    {
        if (!File.Exists(IndexPath)) return Array.Empty<(string, string, string)>();

        var cutoff = DateTimeOffset.Now.AddDays(-withinDays);
        var list = new List<(string fileName, string shaShort, string snippet)>();

        foreach (var line in File.ReadLines(IndexPath).Reverse())
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True) continue;

                if (root.TryGetProperty("ts", out var tsEl) &&
                    DateTimeOffset.TryParse(tsEl.GetString(), out var ts) &&
                    ts < cutoff)
                {
                    break;
                }

                var fileName = root.TryGetProperty("fileName", out var fnEl) ? (fnEl.GetString() ?? "") : "";
                var sha = root.TryGetProperty("sha256", out var shaEl) ? (shaEl.GetString() ?? "") : "";
                var snippet = root.TryGetProperty("snippet", out var snEl) ? (snEl.GetString() ?? "") : "";

                if (string.IsNullOrWhiteSpace(snippet)) continue;

                if (snippet.Length > maxSnippetChars) snippet = snippet.Substring(0, maxSnippetChars) + "…";
                var shaShort = sha.Length >= 8 ? sha.Substring(0, 8) : sha;

                list.Add((fileName, shaShort, snippet));
                if (list.Count >= maxItems) break;
            }
            catch { }
        }

        list.Reverse();
        return list;
    }

    public string BuildSnippetsBlockForPrompt(int maxItems = 6)
    {
        var snippets = ReadRecentSnippets(maxItems: maxItems);
        if (snippets.Count == 0) return "(인덱싱된 스니펫 없음)";

        var sb = new StringBuilder();
        sb.AppendLine("최근 자료 스니펫(요약/관찰 재료):");
        foreach (var (fileName, shaShort, snippet) in snippets)
        {
            sb.AppendLine($"- [{shaShort}] {fileName}");
            sb.AppendLine($"  {snippet}");
        }
        return sb.ToString();
    }
}

public sealed record VaultIndexEntry(
    string ts,
    bool ok,
    string status,
    string reason,
    string? message,
    string kind,
    string sha256,
    string storedPath,
    string fileName,
    string ext,
    long sizeBytes,
    string? snippet
)
{
    public static VaultIndexEntry Ok(VaultItem it, string snippet, string kind)
        => new(DateTimeOffset.Now.ToString("O"), true, "ok", "indexed", null, kind,
            it.sha256, it.storedPath, it.fileName, it.ext, it.sizeBytes, snippet);

    public static VaultIndexEntry Skip(VaultItem it, string reason, string message)
        => new(DateTimeOffset.Now.ToString("O"), false, "skip", reason, message, "skip",
            it.sha256, it.storedPath, it.fileName, it.ext, it.sizeBytes, null);

    public static VaultIndexEntry Fail(VaultItem it, string reason, string message)
        => new(DateTimeOffset.Now.ToString("O"), false, "fail", reason, message, "fail",
            it.sha256, it.storedPath, it.fileName, it.ext, it.sizeBytes, null);
}
