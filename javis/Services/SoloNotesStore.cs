using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services;

public sealed class SoloNotesStore
{
    public string Dir { get; }
    private readonly JsonSerializerOptions _opt = new() { WriteIndented = false };

    public SoloNotesStore(string dataDir)
    {
        Dir = Path.Combine(dataDir, "solo", "notes");
        Directory.CreateDirectory(Dir);
    }

    public async Task AppendAsync(string kind, object data, CancellationToken ct)
    {
        var lineObj = new { ts = DateTimeOffset.Now.ToString("O"), kind, data };
        var line = JsonSerializer.Serialize(lineObj, _opt) + "\n";
        var path = Path.Combine(Dir, $"notes-{DateTime.Now:yyyy-MM-dd}.jsonl");
        await File.AppendAllTextAsync(path, line, Encoding.UTF8, ct);
    }
}
