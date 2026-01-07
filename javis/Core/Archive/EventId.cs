using System;
using System.Security.Cryptography;
using System.Text;

namespace Jarvis.Core.Archive;

public static class EventId
{
    public static string Compute(string kind, string content, DateTimeOffset ts, string? sessionId = null)
    {
        var t = ts.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var raw = $"{kind}|{sessionId ?? ""}|{t}|{content}";

        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
