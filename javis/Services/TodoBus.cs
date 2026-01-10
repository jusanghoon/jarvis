using System;

namespace javis.Services;

public static class TodoBus
{
    public static event Action? Changed;

    public static void PublishChanged()
    {
        try { Changed?.Invoke(); } catch { }
    }
}
