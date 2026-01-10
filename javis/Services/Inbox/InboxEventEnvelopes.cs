using System;
using System.Collections.Generic;

namespace javis.Services.Inbox;

public static class InboxEventEnvelopes
{
    public static Dictionary<string, object?> New(string kind, object? data)
        => new()
        {
            ["schema"] = "jarvis.inbox.v1",
            ["ts"] = DateTimeOffset.Now.ToString("O"),
            ["kind"] = kind,
            ["data"] = data
        };
}
