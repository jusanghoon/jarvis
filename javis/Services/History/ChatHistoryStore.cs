using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services.History;

public sealed class ChatHistoryStore
{
    private readonly string _dir;

    public ChatHistoryStore(string dataDir)
    {
        _dir = Path.Combine(dataDir, "history", "mainchat");
        Directory.CreateDirectory(_dir);
    }

    public async Task<string> SaveSessionAsync(string title, IEnumerable<ChatMessageDto> messages, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var id = now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(_dir, $"{id}.json");

        var session = new ChatSessionDto
        {
            Id = id,
            CreatedAt = now,
            Title = title,
            Messages = messages.ToList()
        };

        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
        return id;
    }

    public async Task SaveOrUpdateSessionAsync(
        string id,
        string title,
        DateTimeOffset createdAt,
        List<ChatMessageDto> messages,
        CancellationToken ct)
    {
        var path = Path.Combine(_dir, $"{id}.json");

        var session = new ChatSessionDto
        {
            Id = id,
            CreatedAt = createdAt,
            Title = title,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
    }

    public IReadOnlyList<ChatSessionInfoDto> ListSessions(int max = 200)
    {
        var files = Directory.EnumerateFiles(_dir, "*.json")
            .OrderByDescending(f => f)
            .Take(max)
            .ToList();

        return files.Select(f =>
        {
            var id = Path.GetFileNameWithoutExtension(f);

            try
            {
                var json = File.ReadAllText(f);
                var session = JsonSerializer.Deserialize<ChatSessionDto>(json);
                if (session != null)
                {
                    return new ChatSessionInfoDto
                    {
                        Id = session.Id ?? id,
                        FilePath = f,
                        CreatedAt = session.CreatedAt != default
                            ? session.CreatedAt
                            : (TryParseIdToDateTimeOffset(id) ?? new DateTimeOffset(File.GetCreationTime(f))),
                        Title = string.IsNullOrWhiteSpace(session.Title) ? (session.Id ?? id) : session.Title
                    };
                }
            }
            catch
            {
                // ignore and fall back
            }

            return new ChatSessionInfoDto
            {
                Id = id,
                FilePath = f,
                CreatedAt = TryParseIdToDateTimeOffset(id) ?? new DateTimeOffset(File.GetCreationTime(f)),
                Title = id
            };
        }).ToList();
    }

    public async Task<ChatSessionDto?> LoadSessionAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(_dir, $"{id}.json");
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ChatSessionDto>(json);
    }

    public void DeleteSession(string id)
    {
        var path = Path.Combine(_dir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    private static DateTimeOffset? TryParseIdToDateTimeOffset(string id)
    {
        try
        {
            var parts = id.Split('_');
            if (parts.Length < 3) return null;

            var date = parts[0];
            var time = parts[1];
            var ms = parts[2];

            var dt = DateTime.ParseExact(
                $"{date}{time}{ms}",
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture);

            return new DateTimeOffset(dt);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class ChatSessionInfoDto
{
    public string Id { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Title { get; set; } = "";
}

public sealed class ChatSessionDto
{
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Title { get; set; } = "";
    public List<ChatMessageDto> Messages { get; set; } = new();
}

public sealed class ChatMessageDto
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset? Ts { get; set; }
}
