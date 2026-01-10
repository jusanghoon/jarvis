using System;
using System.Globalization;
using javis.Models;
using javis.Services.Inbox;
using javis.Services.ChatActions;
using javis.Services;

namespace javis.Services.ChatActions;

public static class TodoActionApplier
{
    public static bool TryApply(TodoAction? action, out string message)
    {
        message = "";
        if (action is null) return false;

        var op = (action.Op ?? "").Trim().ToLowerInvariant();
        if (op is not ("upsert" or "delete"))
        {
            message = "지원하지 않는 todo op";
            return false;
        }

        if (op == "delete")
        {
            Guid id;
            if (Guid.TryParse(action.Id, out var parsed))
            {
                id = parsed;
            }
            else
            {
                // Allow deleting by number from today's open list (1-based)
                var t = (action.Title ?? "").Trim();
                if (!int.TryParse(t, out var idx) || idx <= 0)
                {
                    message = "삭제할 todo id가 올바르지 않음";
                    return false;
                }

                var today = TodoQueryService.GetTodayOpenTodos();
                if (!TodoQueryService.TryResolveIndexToId(today, idx, out id))
                {
                    message = "삭제 번호가 범위를 벗어났음";
                    return false;
                }
            }

            var store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);
            store.Delete(id);

            try
            {
                DailyInbox.Append(InboxKinds.TodoChange, new { op = "delete", id, ts = DateTimeOffset.Now });
            }
            catch { }
            try { javis.Services.TodoBus.PublishChanged(); } catch { }

            message = "할 일을 삭제했습니다.";
            return true;
        }

        // upsert
        if (!DateTime.TryParseExact(action.Date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            message = "todo date 형식이 올바르지 않음";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.Title))
        {
            message = "todo title이 비어있음";
            return false;
        }

        TimeSpan? time = null;
        var timeStr = (action.Time ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(timeStr))
        {
            if (TimeSpan.TryParseExact(timeStr, @"hh\:mm", CultureInfo.InvariantCulture, out var ts))
                time = ts;
            else
            {
                message = "todo time 형식이 올바르지 않음";
                return false;
            }
        }

        var id2 = Guid.NewGuid();
        if (Guid.TryParse(action.Id, out var parsedId)) id2 = parsedId;

        var item = new CalendarTodoItem
        {
            Id = id2,
            Date = date.Date,
            Time = time,
            Title = (action.Title ?? "").Trim(),
            IsDone = action.IsDone ?? false
        };

        var store2 = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);
        store2.Upsert(item);

        try
        {
            DailyInbox.Append(InboxKinds.TodoChange, new
            {
                op = "upsert",
                id = item.Id,
                date = item.Date,
                time = item.Time,
                title = item.Title,
                isDone = item.IsDone,
                ts = DateTimeOffset.Now
            });
        }
        catch { }
        try { javis.Services.TodoBus.PublishChanged(); } catch { }

        message = "할 일을 저장했습니다.";
        return true;
    }
}
