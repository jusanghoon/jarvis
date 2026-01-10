using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using javis.Models;

namespace javis.Services.ChatActions;

public static class TodoQueryService
{
    public static List<CalendarTodoItem> GetTodayOpenTodos()
    {
        var today = DateTime.Today;
        var store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);
        return store.GetByDate(today)
            .Where(x => !x.IsDone)
            .ToList();
    }

    public static List<CalendarTodoItem> GetTodosByDate(DateTime date, bool includeDone = false)
    {
        var store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);
        var list = store.GetByDate(date.Date);
        if (!includeDone) list = list.Where(x => !x.IsDone).ToList();
        return list;
    }

    public static List<CalendarTodoItem> GetTodosInRange(DateTime fromInclusive, int days, bool includeDone = false)
    {
        days = Math.Clamp(days, 1, 60);
        var start = fromInclusive.Date;
        var end = start.AddDays(days).Date;

        var store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);
        var all = store.LoadAll()
            .Where(x => x.Date.Date >= start && x.Date.Date < end)
            .OrderBy(x => x.Date.Date)
            .ThenBy(x => x.Time ?? TimeSpan.MaxValue)
            .ToList();

        if (!includeDone)
            all = all.Where(x => !x.IsDone).ToList();

        return all;
    }

    public static string BuildNumberedListForChat(IEnumerable<CalendarTodoItem> items, string title)
    {
        var list = items.ToList();
        if (list.Count == 0) return $"{title}\n(없음)";

        var lines = new List<string> { title };
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            var time = it.Time is null ? "" : $"{it.Time:hh\\:mm} ";
            var done = it.IsDone ? "DONE" : "TODO";
            lines.Add($"{i + 1}) [{done}] {time}{it.Title}");
        }

        lines.Add("\n삭제: '할일 삭제 2' 처럼 번호로 말해줘.");
        return string.Join("\n", lines);
    }

    public static string BuildGroupedNumberedListForChat(IEnumerable<CalendarTodoItem> items, string title)
    {
        var list = items.ToList();
        if (list.Count == 0) return $"{title}\n(없음)";

        var lines = new List<string> { title };

        DateTime? cur = null;
        int i = 0;
        foreach (var it in list)
        {
            if (cur != it.Date.Date)
            {
                cur = it.Date.Date;
                lines.Add($"\n[{cur:yyyy-MM-dd}] ");
            }

            var time = it.Time is null ? "" : $"{it.Time:hh\\:mm} ";
            var done = it.IsDone ? "DONE" : "TODO";
            lines.Add($"{++i}) [{done}] {time}{it.Title}");
        }

        lines.Add("\n삭제: '할일 삭제 2' 처럼 번호로 말해줘.");
        return string.Join("\n", lines);
    }

    public static bool TryResolveIndexToId(IEnumerable<CalendarTodoItem> items, int index1Based, out Guid id)
    {
        id = default;
        if (index1Based <= 0) return false;

        var list = items.ToList();
        if (index1Based > list.Count) return false;

        id = list[index1Based - 1].Id;
        return true;
    }

    public static bool TryParseNumberedDeleteCommand(string userText, out int index1Based)
    {
        index1Based = 0;
        var t = (userText ?? "").Trim();
        if (t.Length == 0) return false;

        // Accept patterns like:
        // - "할일 삭제 2"
        // - "할 일 삭제 2"
        // - "todo delete 2"
        // - "삭제 2"
        var lower = t.ToLowerInvariant();
        if (!(lower.Contains("삭제") || lower.Contains("delete")))
            return false;

        // Extract the last integer token
        var parts = t.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            {
                index1Based = n;
                return true;
            }
        }

        return false;
    }
}
