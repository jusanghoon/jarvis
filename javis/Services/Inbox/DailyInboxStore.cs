using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace javis.Services.Inbox;

public sealed class DailyInboxStore
{
    private readonly object _lock = new();

    public string UserDir { get; }
    public string InboxDir { get; }
    public string TodayPath { get; }

    public DailyInboxStore(string userDir)
    {
        UserDir = userDir;
        InboxDir = Path.Combine(userDir, "inbox", "daily");
        Directory.CreateDirectory(InboxDir);

        TodayPath = Path.Combine(InboxDir, $"events-{DateTime.Now:yyyy-MM-dd}.jsonl");
    }

    public void Append(string kind, object data)
    {
        if (string.IsNullOrWhiteSpace(kind)) kind = "event";

        var envelope = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.Now.ToString("O"),
            ["kind"] = kind,
            ["data"] = data
        };

        var line = JsonSerializer.Serialize(envelope) + "\n";

        lock (_lock)
        {
            File.AppendAllText(TodayPath, line, Encoding.UTF8);
        }
    }

    public IReadOnlyList<string> ListPendingDays()
    {
        var result = new List<string>();
        if (!Directory.Exists(InboxDir)) return result;

        foreach (var f in Directory.EnumerateFiles(InboxDir, "events-*.jsonl", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(f); // events-YYYY-MM-DD
            var day = name.Substring("events-".Length);
            var doneMarker = Path.Combine(InboxDir, $"events-{day}.compacted.ok");
            if (!File.Exists(doneMarker))
                result.Add(day);
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    public string GetInboxPath(string day)
        => Path.Combine(InboxDir, $"events-{day}.jsonl");

    public string GetDoneMarkerPath(string day)
        => Path.Combine(InboxDir, $"events-{day}.compacted.ok");

    public void MarkDone(string day)
    {
        var marker = GetDoneMarkerPath(day);
        try { File.WriteAllText(marker, DateTimeOffset.Now.ToString("O"), Encoding.UTF8); }
        catch { }
    }
}
