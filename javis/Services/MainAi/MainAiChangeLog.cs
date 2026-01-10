using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace javis.Services.MainAi;

public static class MainAiChangeLog
{
    public static void Append(string userId, string kind, IReadOnlyList<string> changedKeys)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId)) return;

            var userDir = javis.Services.UserProfileService.Instance.GetUserDataDir(userId);
            var dir = Path.Combine(userDir, "profiles", "_mainai");

            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"{userId}.changes.log");
            var line = $"{DateTimeOffset.Now:O}\t{(kind ?? "").Trim()}\t{string.Join(",", changedKeys.Distinct(StringComparer.OrdinalIgnoreCase))}";

            File.AppendAllText(path, line + "\n");

            TrimToLastLines(path, 200);
        }
        catch
        {
            // ignore
        }
    }

    private static void TrimToLastLines(string path, int keep)
    {
        try
        {
            if (keep <= 0) return;
            if (!File.Exists(path)) return;

            var lines = File.ReadAllLines(path);
            if (lines.Length <= keep) return;

            var tail = lines.Skip(Math.Max(0, lines.Length - keep)).ToArray();
            File.WriteAllLines(path, tail);
        }
        catch
        {
            // ignore
        }
    }
}
