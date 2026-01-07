using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using javis.Services;

namespace Jarvis.Core.Archive;

public sealed class ArchiveStore
{
    private readonly AuditLogger _audit;
    private readonly int _dedupeWindow;
    private readonly object _gate = new();

    private readonly Queue<string> _recentIds = new();
    private readonly HashSet<string> _recentSet = new(StringComparer.Ordinal);

    private readonly object _bufGate = new();
    private readonly List<ActiveItem> _activeBuf = new();
    private int _activeCharSum = 0;
    private bool _fossilizeInFlight = false;

    private const int FossilMaxItems = 80;
    private const int FossilMaxChars = 60_000;

    private const int FossilMaxSummaryChars = 12_000;
    private const int FossilBufTruncChars = 4_096;

    public Func<IReadOnlyList<ActiveItem>, CancellationToken, Task<string>>? SummarizeAsync { get; set; }

    public bool WriteTransitionEvent { get; set; } = true;

    public readonly record struct ActiveItem(
        string EventId,
        long TsUnixMs,
        string Role,
        string MetaKind,
        string ContentForSummary
    );

    public ArchiveStore(AuditLogger audit, int dedupeWindow = 256)
    {
        _audit = audit;
        _dedupeWindow = Math.Max(32, dedupeWindow);
    }

    private bool Seen(string eventId)
    {
        lock (_gate)
        {
            if (_recentSet.Contains(eventId))
                return true;

            _recentIds.Enqueue(eventId);
            _recentSet.Add(eventId);

            while (_recentIds.Count > _dedupeWindow)
            {
                var old = _recentIds.Dequeue();
                _recentSet.Remove(old);
            }

            return false;
        }
    }

    public bool Record(
        string content,
        GEMSRole role,
        KnowledgeState state,
        string? sessionId = null,
        Dictionary<string, object?>? meta = null)
    {
        var ts = DateTimeOffset.Now;

        meta ??= new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(sessionId))
            meta["sessionId"] = sessionId;

        meta["contentHash"] = ContentHash.Sha256Hex(content);
        meta["contentLen"] = content?.Length ?? 0;

        var eventId = EventId.Compute(kind: "archive", content: content, ts: ts, sessionId: sessionId);
        if (Seen(eventId))
            return false;

        var metaKind =
            meta.TryGetValue("kind", out var mk) && mk is string s && !string.IsNullOrWhiteSpace(s)
                ? s
                : "note";

        var ok = _audit.WriteArchive(
            ts: ts,
            eventId: eventId,
            content: content,
            role: role.ToString(),
            state: state.ToString(),
            meta: meta);

        if (!ok) return false;

        if (state == KnowledgeState.Active)
        {
            BufferActive(eventId, ts, role.ToString(), metaKind, content);
            TryKickFossilize(sessionId);
        }

        return true;
    }

    private static string TruncForBuf(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "…";
    }

    private void BufferActive(string eventId, DateTimeOffset ts, string role, string metaKind, string content)
    {
        var c = TruncForBuf(content, FossilBufTruncChars);
        lock (_bufGate)
        {
            _activeBuf.Add(new ActiveItem(
                EventId: eventId,
                TsUnixMs: ts.ToUnixTimeMilliseconds(),
                Role: role,
                MetaKind: metaKind,
                ContentForSummary: c));

            _activeCharSum += content?.Length ?? 0;
        }
    }

    private void TryKickFossilize(string? sessionId)
    {
        List<ActiveItem>? snapshot = null;

        lock (_bufGate)
        {
            if (_fossilizeInFlight) return;

            if (_activeBuf.Count < FossilMaxItems && _activeCharSum < FossilMaxChars)
                return;

            _fossilizeInFlight = true;

            snapshot = new List<ActiveItem>(_activeBuf);
            _activeBuf.Clear();
            _activeCharSum = 0;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await FossilizeSnapshotAsync(snapshot!, sessionId, CancellationToken.None);
            }
            catch
            {
                // fossilize must never crash app
            }
            finally
            {
                lock (_bufGate)
                {
                    _fossilizeInFlight = false;
                }

                TryKickFossilize(sessionId);
            }
        });
    }

    private async Task FossilizeSnapshotAsync(
        IReadOnlyList<ActiveItem> snapshot,
        string? sessionId,
        CancellationToken ct)
    {
        if (snapshot.Count == 0) return;

        var from = snapshot[0].TsUnixMs;
        var to = snapshot[^1].TsUnixMs;

        var idsJoined = string.Join(",", snapshot.Select(x => x.EventId));
        var sourceIdsHash = ContentHash.Sha256Hex(idsJoined);

        string summary;
        long summaryMs;
        var summaryEngine = SummarizeAsync is null ? "rule" : "custom";

        var sw = Stopwatch.StartNew();
        try
        {
            if (SummarizeAsync is not null)
            {
                summary = await SummarizeAsync(snapshot, ct);
            }
            else
            {
                summary = RuleBasedSummary(snapshot);
            }
        }
        finally
        {
            sw.Stop();
            summaryMs = sw.ElapsedMilliseconds;
        }

        summary = (summary ?? "").Trim();
        if (summary.Length > FossilMaxSummaryChars)
            summary = summary.Substring(0, FossilMaxSummaryChars) + "…";

        Record(
            content: summary,
            role: GEMSRole.Recorder,
            state: KnowledgeState.Fossil,
            sessionId: sessionId,
            meta: new Dictionary<string, object?>
            {
                ["kind"] = "fossilize",
                ["summaryEngine"] = summaryEngine,
                ["summaryMs"] = summaryMs,
                ["sourceCount"] = snapshot.Count,
                ["fromTsUnixMs"] = from,
                ["toTsUnixMs"] = to,
                ["sourceIdsHash"] = sourceIdsHash,
                ["sourceIdSample"] = snapshot.Take(5).Select(x => x.EventId).ToArray()
            });

        if (WriteTransitionEvent)
        {
            Record(
                content: $"STATE_TRANSITION: Active -> Standby (sourceIdsHash={sourceIdsHash})",
                role: GEMSRole.Recorder,
                state: KnowledgeState.Standby,
                sessionId: sessionId,
                meta: new Dictionary<string, object?>
                {
                    ["kind"] = "state.transition",
                    ["toState"] = "Standby",
                    ["sourceCount"] = snapshot.Count,
                    ["fromTsUnixMs"] = from,
                    ["toTsUnixMs"] = to,
                    ["sourceIdsHash"] = sourceIdsHash
                });
        }
    }

    private static string RuleBasedSummary(IReadOnlyList<ActiveItem> items)
    {
        var dist = items
            .GroupBy(x => string.IsNullOrWhiteSpace(x.MetaKind) ? "unknown" : x.MetaKind)
            .OrderByDescending(g => g.Count())
            .Take(12)
            .Select(g => $"{g.Key}={g.Count()}");

        var head = items.Take(8).ToList();
        var tail = items.Count <= 16 ? new List<ActiveItem>() : items.Skip(Math.Max(0, items.Count - 8)).ToList();

        var sb = new StringBuilder(4096);
        sb.AppendLine($"FOSSIL v1 :: items={items.Count}");
        sb.AppendLine($"kinds :: {string.Join(", ", dist)}");
        sb.AppendLine();
        sb.AppendLine("head ::");
        foreach (var x in head)
            sb.AppendLine($"- [{x.MetaKind}] ({x.Role}) {x.ContentForSummary}");

        if (tail.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("tail ::");
            foreach (var x in tail)
                sb.AppendLine($"- [{x.MetaKind}] ({x.Role}) {x.ContentForSummary}");
        }

        return sb.ToString();
    }
}
