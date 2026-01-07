using System;

namespace javis.Services;

internal static class JsonUtil
{
    public static string ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        if (start < 0) throw new Exception("JSON 시작 '{' 없음");

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
        throw new Exception("JSON 추출 실패");
    }
}
