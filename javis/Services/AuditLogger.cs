using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace javis.Services;

public sealed class AuditLogger : IAsyncDisposable
{
    private readonly Channel<string> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private readonly JsonSerializerOptions _jsonOpt = new() { WriteIndented = false };

    public string LogsDir { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public int MaxFieldChars { get; set; } = 50_000;
    public bool Enabled { get; set; } = true;

    public AuditLogger(string logsDir)
    {
        LogsDir = logsDir;
        Directory.CreateDirectory(LogsDir);

        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _writerTask = Task.Run(WriterLoop);
    }

    public void Log(string kind, object data)
    {
        if (!Enabled) return;

        var payload = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.Now.ToString("O"),
            ["session"] = SessionId,
            ["kind"] = kind,
            ["data"] = TruncateObject(data)
        };

        var line = JsonSerializer.Serialize(payload, _jsonOpt);
        _channel.Writer.TryWrite(line);
    }

    public void LogText(string kind, string text, object? extra = null)
    {
        if (!Enabled) return;

        var payload = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.Now.ToString("O"),
            ["session"] = SessionId,
            ["kind"] = kind,
            ["text"] = Trunc(text),
            ["extra"] = extra is null ? null : TruncateObject(extra)
        };

        var line = JsonSerializer.Serialize(payload, _jsonOpt);
        _channel.Writer.TryWrite(line);
    }

    private async Task WriterLoop()
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_cts.Token))
            {
                var sb = new StringBuilder(256 * 1024);

                while (_channel.Reader.TryRead(out var line))
                {
                    sb.AppendLine(line);
                    if (sb.Length > 512 * 1024) break;
                }

                if (sb.Length > 0)
                {
                    var file = Path.Combine(LogsDir, $"audit-{DateTime.Now:yyyy-MM-dd}.jsonl");
                    await File.AppendAllTextAsync(file, sb.ToString(), Encoding.UTF8, _cts.Token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // logging must never crash the app
        }
    }

    public void PruneOldLogs(int keepDays = 30)
    {
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-keepDays);
            foreach (var f in Directory.EnumerateFiles(LogsDir, "audit-*.jsonl"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (name.Length >= "audit-YYYY-MM-DD".Length)
                {
                    var datePart = name.Substring("audit-".Length);
                    if (DateTime.TryParse(datePart, out var dt) && dt.Date < cutoff)
                        File.Delete(f);
                }
            }
        }
        catch { }
    }

    private object? TruncateObject(object? obj)
    {
        if (obj is null) return null;

        if (obj is string s) return Trunc(s);

        try
        {
            var json = JsonSerializer.Serialize(obj, _jsonOpt);
            if (json.Length <= MaxFieldChars) return JsonSerializer.Deserialize<object>(json);
            return new { truncated = true, len = json.Length, head = json.Substring(0, MaxFieldChars) };
        }
        catch
        {
            return new { note = "serialize_failed", type = obj.GetType().FullName };
        }
    }

    private string Trunc(string s)
        => s.Length <= MaxFieldChars ? s : s.Substring(0, MaxFieldChars);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { await _writerTask; } catch { }
        _cts.Dispose();
    }
}
