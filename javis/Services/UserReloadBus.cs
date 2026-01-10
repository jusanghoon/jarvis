using System;

namespace javis.Services;

public static class UserReloadBus
{
    public static event Action<string>? ActiveUserChanged;

    public static void PublishActiveUserChanged(string userId)
    {
        try { ActiveUserChanged?.Invoke(userId); }
        catch { }
    }
}
