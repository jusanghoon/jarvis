using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace javis.Services.Inbox;

public sealed class DailyInboxWriter
{
    private readonly object _lock = new();

    public string UserDir { get; }

    public DailyInboxWriter(string userDir)
    {
        UserDir = userDir;
    }

    public void Append(string kind, object? data)
    {
        var userDir = UserDir;
        var day = DateTime.Today;

        var eventsPath = InboxPaths.GetEventsPath(userDir, day);
        var indexPath = InboxPaths.GetIndexPath(userDir, day);

        var env = InboxEventEnvelopes.New(kind, data);
        var json = JsonSerializer.Serialize(env);

        // Always write a single line with \n; keep UTF8 (no BOM) for safe appends.
        var bytes = Encoding.UTF8.GetBytes(json + "\n");

        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(eventsPath)!);

            long start;
            using (var fs = new FileStream(eventsPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                fs.Seek(0, SeekOrigin.End);
                start = fs.Position;
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            try
            {
                var idx = new DailyInboxIndex(indexPath);
                idx.Append(kind, start, bytes.Length);
            }
            catch
            {
                // index is best-effort; never block event write
            }
        }
    }
}
