using System;

namespace javis.Services;

internal static class JsonUtil
{
    public static string ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        if (start < 0) return string.Empty;

        int depth = 0;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0) return s.Substring(start, i - start + 1);
            }
        }
        return string.Empty;
    }

    public static string ExtractJsonObjectContaining(string s, string mustContain)
    {
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(mustContain))
            return string.Empty;

        var start = 0;
        while (start >= 0 && start < s.Length)
        {
            start = s.IndexOf('{', start);
            if (start < 0) return string.Empty;

            int depth = 0;
            for (int i = start; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var candidate = s.Substring(start, i - start + 1);
                        if (candidate.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
                            return candidate;
                        break;
                    }
                }
            }

            start++;
        }

        return string.Empty;
    }
}
