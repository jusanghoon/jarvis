using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using javis.Models;
using javis.ViewModels;

namespace javis.Services;

internal static class TodoMapSync
{
    private static readonly Regex LocationTag = new(@"@(?<loc>[^@\n\r]{2,64})", RegexOptions.Compiled);

    public static void TrySyncFromTodos(IEnumerable<CalendarTodoItem> todos)
    {
        try
        {
            var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in todos)
            {
                var title = t.Title ?? "";
                foreach (Match m in LocationTag.Matches(title))
                {
                    var loc = (m.Groups["loc"].Value ?? "").Trim();
                    if (loc.Length >= 2)
                        locations.Add(loc);
                }
            }

            if (locations.Count == 0)
                return;

            var store = new MapPinsStore();
            var pins = store.Load().ToList();
            var existing = new HashSet<string>(pins.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var loc in locations)
            {
                if (existing.Contains(loc))
                    continue;

                var idx = pins.Count;
                pins.Add(new MapPin
                {
                    Name = loc,
                    X = 20 + (idx * 22) % 520,
                    Y = 40 + ((idx * 31) % 300)
                });
            }

            store.Save(pins);
        }
        catch { }
    }
}
