using System;
using System.IO;

namespace javis.Services.Inbox;

public static class InboxPaths
{
    public static string GetInboxDir(string userDir)
        => Path.Combine(userDir, "inbox", "daily");

    public static string GetEventsPath(string userDir, DateTime day)
    {
        var dir = GetInboxDir(userDir);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"events-{day:yyyy-MM-dd}.jsonl");
    }

    public static string GetIndexPath(string userDir, DateTime day)
    {
        var dir = GetInboxDir(userDir);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"events-{day:yyyy-MM-dd}.idx.jsonl");
    }

    public static string GetDoneMarker(string userDir, DateTime day)
    {
        var dir = GetInboxDir(userDir);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"events-{day:yyyy-MM-dd}.compacted.ok");
    }
}
