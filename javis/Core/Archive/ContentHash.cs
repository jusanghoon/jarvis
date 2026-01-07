using System;
using System.Security.Cryptography;
using System.Text;

namespace Jarvis.Core.Archive;

public static class ContentHash
{
    public static string Sha256Hex(string? s)
    {
        s ??= "";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(hash);
    }
}
