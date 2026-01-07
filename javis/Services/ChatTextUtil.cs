using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace javis.Services;

internal static class ChatTextUtil
{
    public static string SanitizeUiText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\t' || ch == '\r' || ch == '\n') { sb.Append(ch); continue; }
            if (ch < ' ' || ch == '\u007F') { sb.Append(' '); continue; }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    public static string TrimMax(string s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    public static List<string> ReadStringArray(JsonElement root, string name, int take, int maxLen)
    {
        var list = new List<string>();
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return list;
        foreach (var it in el.EnumerateArray())
        {
            var s = it.ValueKind == JsonValueKind.String ? (it.GetString() ?? "") : it.ToString();
            s = TrimMax(s, maxLen);
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            if (list.Count >= take) break;
        }
        return list;
    }
}
