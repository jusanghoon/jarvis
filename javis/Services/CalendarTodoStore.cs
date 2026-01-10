using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using javis.Models;

namespace javis.Services;

public sealed class CalendarTodoStore
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public CalendarTodoStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis"))
    {
    }

    public CalendarTodoStore(string dataDir)
    {
        var dir = (dataDir ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Jarvis");
        }

        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "calendar_todos.json");
    }

    public List<CalendarTodoItem> LoadAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath)) return new List<CalendarTodoItem>();
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<CalendarTodoItem>>(json, JsonOpts) ?? new();
            }
            catch
            {
                return new List<CalendarTodoItem>();
            }
        }
    }

    public void SaveAll(List<CalendarTodoItem> items)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(items, JsonOpts);
            File.WriteAllText(_filePath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
    }

    public List<CalendarTodoItem> GetByDate(DateTime date)
    {
        var d = date.Date;
        return LoadAll()
            .Where(x => x.Date.Date == d)
            .OrderBy(x => x.Time ?? TimeSpan.MaxValue)
            .ThenBy(x => x.Title)
            .ToList();
    }

    public void Upsert(CalendarTodoItem item)
    {
        lock (_lock)
        {
            var all = LoadAll();
            var idx = all.FindIndex(x => x.Id == item.Id);
            if (idx >= 0) all[idx] = item;
            else all.Add(item);

            SaveAll(all);
        }
    }

    public void Delete(Guid id)
    {
        lock (_lock)
        {
            var all = LoadAll();
            all.RemoveAll(x => x.Id == id);
            SaveAll(all);
        }
    }

    public List<CalendarTodoItem> GetUpcoming(DateTime from, int days)
    {
        var start = from.Date;
        var end = start.AddDays(days).Date;

        return LoadAll()
            .Where(x => x.Date.Date >= start && x.Date.Date < end)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Time ?? TimeSpan.MaxValue)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };
}
