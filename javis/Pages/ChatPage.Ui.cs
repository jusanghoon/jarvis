using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace javis.Pages;

public partial class ChatPage : Page
{
    private Task UiAsync(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(action, DispatcherPriority.Background).Task;
    }

    private Task<T> UiAsync<T>(Func<T> func)
    {
        if (Dispatcher.CheckAccess())
            return Task.FromResult(func());

        return Dispatcher.InvokeAsync(func, DispatcherPriority.Background).Task;
    }

    private static Dictionary<string, string> ReadVars(JsonElement root)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("vars", out var varsEl) && varsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in varsEl.EnumerateObject())
                vars[p.Name] = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "") : p.Value.ToString();
        }

        return vars;
    }

    private void ScrollToEnd()
    {
        _ = UiAsync(() =>
        {
            if (ChatList.Items.Count > 0)
                ChatList.ScrollIntoView(ChatList.Items[^1]);
        });
    }
}
