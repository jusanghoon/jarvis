using System;
using System.Globalization;

namespace javis.Services.ChatActions;

public static class KstDateParser
{
    public static DateTime TodayKstDate()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        return now.Date;
    }

    public static bool TryParseKoreanRelativeDate(string? text, out DateTime date)
    {
        date = default;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return false;

        var today = TodayKstDate();

        if (t.Contains("오늘", StringComparison.OrdinalIgnoreCase)) { date = today; return true; }
        if (t.Contains("내일", StringComparison.OrdinalIgnoreCase)) { date = today.AddDays(1); return true; }
        if (t.Contains("모레", StringComparison.OrdinalIgnoreCase)) { date = today.AddDays(2); return true; }

        // explicit yyyy-MM-dd
        if (DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            date = d.Date;
            return true;
        }

        return false;
    }
}
