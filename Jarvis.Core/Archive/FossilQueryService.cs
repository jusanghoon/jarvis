using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jarvis.Core.Archive;

public sealed record FossilEntry(
    long TsUnixMs,
    DateTimeOffset Ts,
    string EventId,
    string Content,
    int SourceCount,
    string SourceIdsHash
);

public sealed class FossilQueryService
{
    private readonly string _logsDir;

    private readonly object _cacheGate = new();
    private (DateTimeOffset ts, string key, IReadOnlyList<FossilEntry> value)? _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public FossilQueryService(string logsDir)
    {
        _logsDir = logsDir;
    }

    public Task<IReadOnlyList<FossilEntry>> GetRecentFossilsAsync(
        int n,
        int scanDays = 7,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        if (n <= 0) return Task.FromResult<IReadOnlyList<FossilEntry>>(Array.Empty<FossilEntry>());
        scanDays = Math.Clamp(scanDays, 1, 60);

        var key = $"{n}|{scanDays}|{sessionId ?? ""}";

        lock (_cacheGate)
        {
            if (_cache is { } c &&
                string.Equals(c.key, key, StringComparison.Ordinal) &&
                (DateTimeOffset.Now - c.ts) <= CacheTtl)
            {
                return Task.FromResult(c.value);
            }
        }

        return Task.Run(() =>
        {
            var result = GetRecentFossilsCore(n, scanDays, sessionId, ct);
            lock (_cacheGate)
                _cache = (DateTimeOffset.Now, key, result);
            return result;
        }, ct);
    }

    private IReadOnlyList<FossilEntry> GetRecentFossilsCore(
        int n,
        int scanDays,
        string? sessionId,
        CancellationToken ct)
    {
        if (!Directory.Exists(_logsDir))
            return Array.Empty<FossilEntry>();

        var files = EnumerateAuditFiles(_logsDir, scanDays)
            .OrderByDescending(x => x.DateKey)
            .Select(x => x.Path)
            .ToList();

        var pq = new PriorityQueue<FossilEntry, long>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var fs = File.OpenRead(file);
                using var sr = new StreamReader(fs);

                string? line;
                while ((line = sr.ReadLine()) is not null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (!TryParseFossil(line, sessionId, out var fossil))
                        continue;

                    pq.Enqueue(fossil!, fossil!.TsUnixMs);
                    if (pq.Count > n)
                        pq.Dequeue();
                }
            }
            catch
            {
                // Reader must never crash the app.
            }
        }

        var list = new List<FossilEntry>(pq.Count);
        while (pq.TryDequeue(out var item, out _))
            list.Add(item);

        list.Sort((a, b) => b.TsUnixMs.CompareTo(a.TsUnixMs));
        return list;
    }

    private static bool TryParseFossil(string line, string? sessionId, out FossilEntry? fossil)
    {
        fossil = null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("kind", out var kindEl) ||
                !EqualsCI(kindEl.GetString(), "archive"))
                return false;

            if (!root.TryGetProperty("schema", out var schemaEl) ||
                !string.Equals(schemaEl.GetString(), "jarvis.archive.v1", StringComparison.Ordinal))
                return false;

            if (!root.TryGetProperty("state", out var stateEl) ||
                !EqualsCI(stateEl.GetString(), "Fossil"))
                return false;

            if (!root.TryGetProperty("meta", out var metaEl) ||
                metaEl.ValueKind != JsonValueKind.Object)
                return false;

            if (!metaEl.TryGetProperty("kind", out var mkEl) ||
                !EqualsCI(mkEl.GetString(), "fossilize"))
                return false;

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                if (!metaEl.TryGetProperty("sessionId", out var sidEl) ||
                    !string.Equals(sidEl.GetString(), sessionId, StringComparison.Ordinal))
                    return false;
            }

            if (!root.TryGetProperty("tsUnixMs", out var tuEl) || !tuEl.TryGetInt64(out var tsUnixMs) || tsUnixMs <= 0)
                return false;

            var ts = root.TryGetProperty("ts", out var tsEl) && tsEl.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(tsEl.GetString()!, CultureInfo.InvariantCulture)
                : DateTimeOffset.FromUnixTimeMilliseconds(tsUnixMs);

            var eventId = root.TryGetProperty("eventId", out var idEl) ? (idEl.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(eventId))
                return false;

            var content = root.TryGetProperty("content", out var cEl) ? (cEl.GetString() ?? "") : "";

            var sourceCount = metaEl.TryGetProperty("sourceCount", out var scEl) && scEl.TryGetInt32(out var sc)
                ? sc
                : 0;

            var sourceIdsHash = metaEl.TryGetProperty("sourceIdsHash", out var hEl) ? (hEl.GetString() ?? "") : "";

            fossil = new FossilEntry(tsUnixMs, ts, eventId, content, sourceCount, sourceIdsHash);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool EqualsCI(string? a, string b)
        => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private sealed record AuditFile(string Path, int DateKey);

    private static IEnumerable<AuditFile> EnumerateAuditFiles(string logsDir, int scanDays)
    {
        var today = DateTime.Today;
        var minDate = today.AddDays(-(scanDays - 1));

        foreach (var path in Directory.EnumerateFiles(logsDir, "audit-*.jsonl"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length < "audit-YYYY-MM-DD".Length) continue;

            var datePart = name.Substring("audit-".Length);
            if (!DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                continue;

            if (d.Date < minDate.Date) continue;

            var key = d.Year * 10000 + d.Month * 100 + d.Day;
            yield return new AuditFile(path, key);
        }
    }
}
