using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using javis.Services.History;

namespace javis.Services.Inbox;

public static class DailyInboxCompactor
{
    public static async Task<int> TryCompactAllPendingAsync(CancellationToken ct)
    {
        var userDir = UserProfileService.Instance.ActiveUserDataDir;
        var inbox = new DailyInboxStore(userDir);
        var pending = inbox.ListPendingDays();

        int done = 0;
        foreach (var day in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryCompactDayAsync(userDir, day, ct)) done++;
        }

        return done;
    }

    private sealed record ChatInboxMsg(DateTimeOffset ts, string room, string role, string text);
    private sealed record InboxCheckpoint(long byteOffset, DateTimeOffset updatedAt);

    private static async Task<bool> TryCompactDayAsync(string userDir, string day, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return false;

        var src = InboxPaths.GetEventsPath(userDir, d);
        if (!File.Exists(src))
        {
            try { File.WriteAllText(InboxPaths.GetDoneMarker(userDir, d), DateTimeOffset.Now.ToString("O")); } catch { }
            return true;
        }

        try
        {
            var chat = new List<ChatInboxMsg>();
            var boundaries = new List<(DateTimeOffset at, string evt)>();

            var cp = ReadCheckpoint(userDir, d);

            // read only from last byte offset
            using (var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (cp.byteOffset > 0 && cp.byteOffset < fs.Length)
                    fs.Seek(cp.byteOffset, SeekOrigin.Begin);
                using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var kind = root.TryGetProperty("kind", out var kEl) ? (kEl.GetString() ?? "") : "";

                    if (string.Equals(kind, InboxKinds.ChatMessage, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                            continue;

                        var room = data.TryGetProperty("room", out var rEl) ? (rEl.GetString() ?? "") : "";
                        var role = data.TryGetProperty("role", out var roleEl) ? (roleEl.GetString() ?? "") : "";
                        var text = data.TryGetProperty("text", out var tEl) ? (tEl.GetString() ?? "") : "";

                        var ts = DateTimeOffset.Now;
                        if (data.TryGetProperty("ts", out var tsEl))
                        {
                            if (tsEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(tsEl.GetString(), out var parsed))
                                ts = parsed;
                            else if (DateTimeOffset.TryParse(tsEl.ToString(), out var parsed2))
                                ts = parsed2;
                        }

                        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(text))
                            continue;

                        chat.Add(new ChatInboxMsg(ts, room, role, text));
                        continue;
                    }

                    if (string.Equals(kind, InboxKinds.ChatRequest, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kind, InboxKinds.DuoRequest, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kind, InboxKinds.SoloRequest, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
                            var at = data.TryGetProperty("at", out var atEl) && atEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(atEl.GetString(), out var atParsed)
                                ? atParsed
                                : DateTimeOffset.Now;
                            var evtName = data.TryGetProperty("evt", out var eEl) ? (eEl.GetString() ?? "") : "";
                            boundaries.Add((at, evtName));
                        }
                        catch { }
                    }
                }

                // after reading: checkpoint to current file position
                WriteCheckpoint(userDir, d, fs.Position);
            }

            if (chat.Count == 0)
            {
                try { File.WriteAllText(InboxPaths.GetDoneMarker(userDir, d), DateTimeOffset.Now.ToString("O")); } catch { }
                return true;
            }

            boundaries = boundaries.OrderBy(x => x.at).ToList();
            chat = chat.OrderBy(x => x.ts).ToList();

            var store = new ChatHistoryStore(userDir);

            // Build segments using request starts (room-specific when available).
            static List<DateTimeOffset> BuildStarts(List<(DateTimeOffset at, string evt)> b, List<ChatInboxMsg> msgs, string roomKey)
            {
                var startEvt = roomKey.Equals("duo", StringComparison.OrdinalIgnoreCase)
                    ? "DuoRequestStarted"
                    : roomKey.Equals("solo", StringComparison.OrdinalIgnoreCase)
                        ? "SoloRequestStarted"
                        : nameof(javis.Services.MainAi.ChatRequestStarted);

                var starts = b.Where(x => string.Equals(x.evt, startEvt, StringComparison.OrdinalIgnoreCase))
                              .Select(x => x.at)
                              .ToList();

                if (starts.Count == 0)
                    starts.Add(msgs.Min(m => m.ts));

                return starts.Distinct().OrderBy(x => x).ToList();
            }

            // Build starts list per room
            foreach (var roomKey in chat.Select(m => (m.room ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var roomMsgs = chat.Where(m => string.Equals(m.room, roomKey, StringComparison.OrdinalIgnoreCase)).ToList();
                if (roomMsgs.Count == 0) continue;

                var starts = BuildStarts(boundaries, roomMsgs, roomKey);
                for (int i = 0; i < starts.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var start = starts[i];
                    var end = (i + 1 < starts.Count) ? starts[i + 1] : DateTimeOffset.MaxValue;

                    var seg = roomMsgs.Where(m => m.ts >= start && m.ts < end).ToList();
                    if (seg.Count == 0) continue;
                    if (!seg.Any(m => string.Equals(m.role, "user", StringComparison.OrdinalIgnoreCase))) continue;

                    var createdAt = seg.Min(m => m.ts);
                    var id = createdAt.ToString("yyyyMMdd_HHmmss_fff") + $"_inbox_{roomKey}";

                    var firstUser = seg.FirstOrDefault(m => string.Equals(m.role, "user", StringComparison.OrdinalIgnoreCase))?.text ?? "대화";
                    firstUser = (firstUser ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                    if (firstUser.Length > 42) firstUser = firstUser[..42];
                    var title = string.IsNullOrWhiteSpace(firstUser) ? "대화" : firstUser;

                    var dto = seg
                        .Select(m => new ChatMessageDto { Role = m.role, Text = m.text, Ts = m.ts })
                        .ToList();

                    await store.SaveOrUpdateSessionAsync(id, title, createdAt, dto, ct);
                }
            }

            try { File.WriteAllText(InboxPaths.GetDoneMarker(userDir, d), DateTimeOffset.Now.ToString("O")); } catch { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCheckpointPath(string userDir, DateTime day)
        => Path.Combine(InboxPaths.GetInboxDir(userDir), $"events-{day:yyyy-MM-dd}.cp.json");

    private static InboxCheckpoint ReadCheckpoint(string userDir, DateTime day)
    {
        try
        {
            var path = GetCheckpointPath(userDir, day);
            if (!File.Exists(path)) return new InboxCheckpoint(0, DateTimeOffset.MinValue);
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var off = root.TryGetProperty("byteOffset", out var oEl) ? oEl.GetInt64() : 0;
            var at = root.TryGetProperty("updatedAt", out var aEl) && DateTimeOffset.TryParse(aEl.GetString(), out var parsed) ? parsed : DateTimeOffset.MinValue;
            return new InboxCheckpoint(Math.Max(0, off), at);
        }
        catch
        {
            return new InboxCheckpoint(0, DateTimeOffset.MinValue);
        }
    }

    private static void WriteCheckpoint(string userDir, DateTime day, long byteOffset)
    {
        try
        {
            var path = GetCheckpointPath(userDir, day);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var obj = new { byteOffset, updatedAt = DateTimeOffset.Now.ToString("O") };
            File.WriteAllText(path, JsonSerializer.Serialize(obj), Encoding.UTF8);
        }
        catch { }
    }
}
