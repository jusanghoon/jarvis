using System;

namespace javis.Services.Inbox;

public static class DailyInbox
{
    public static void Append(string kind, object? data)
    {
        try
        {
            var userDir = UserProfileService.Instance.ActiveUserDataDir;
            var w = new DailyInboxWriter(userDir);
            w.Append(kind, data);
        }
        catch
        {
            // never block app logic on inbox write
        }
    }
}
