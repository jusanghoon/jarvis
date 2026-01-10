using System;
using System.IO;
using System.Text;

namespace javis.Services.Inbox;

public sealed class DailyInboxIndex
{
    private readonly object _lock = new();
    private readonly string _indexPath;

    public DailyInboxIndex(string indexPath)
    {
        _indexPath = indexPath;
        var dir = Path.GetDirectoryName(_indexPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
    }

    public void Append(string kind, long startOffset, int byteLen)
    {
        // index line format: kind\tstart\tlen\n
        var line = $"{kind}\t{startOffset}\t{byteLen}\n";
        lock (_lock)
        {
            File.AppendAllText(_indexPath, line, Encoding.UTF8);
        }
    }
}
