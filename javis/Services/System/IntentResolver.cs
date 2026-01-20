using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jarvis.Core.System;

namespace javis.Services.SystemActions;

public sealed class IntentResolution
{
    public bool Handled { get; init; }
    public string? ReplyText { get; init; }

    public bool NeedsUserInput { get; init; }
    public string? MissingField { get; init; }

    public ExecutionResult? Execution { get; init; }

    public string? AuditKind { get; init; }

    public object? AuditPayload { get; init; }
}

public sealed class IntentResolver
{
    private static readonly Regex ProjectOpenRegex = new(
        pattern: "(프로젝트.*열어줘|프로젝트 열어줘|내 프로젝트 열어줘|프로젝트 열어|open.*project)",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string ExpandDynamicPath(string raw)
    {
        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0) return "";

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            return expanded;
        }
        catch
        {
            return raw;
        }
    }

    public async Task<IntentResolution> TryResolveAsync(
        string userText,
        UserProfileService profiles,
        ISystemActionExecutor system,
        CancellationToken ct = default)
    {
        userText = (userText ?? "").Trim();
        if (userText.Length == 0)
            return new IntentResolution { Handled = false };

        if (!ProjectOpenRegex.IsMatch(userText))
            return new IntentResolution { Handled = false };

        var profile = profiles.TryGetActiveProfile();
        if (profile is null)
            return new IntentResolution { Handled = true, ReplyText = "프로필을 불러오지 못했어.", Execution = ExecutionResult.Fail("No active profile") };

        var deviceId = profile.LastDeviceDiagnostics?.Fingerprint?.DeviceId ?? "unknown";

        profile.Fields.TryGetValue("Preferred_Editor", out var editorPath);
        profile.Fields.TryGetValue("Default_Project_Path", out var projectPath);

        editorPath = ExpandDynamicPath(editorPath ?? "");
        projectPath = ExpandDynamicPath(projectPath ?? "");

        if (string.IsNullOrWhiteSpace(editorPath) || string.IsNullOrWhiteSpace(projectPath))
        {
            var missing = string.IsNullOrWhiteSpace(editorPath) ? "Preferred_Editor" : "Default_Project_Path";
            return new IntentResolution
            {
                Handled = true,
                NeedsUserInput = true,
                MissingField = missing,
                ReplyText = "어떤 에디터와 경로를 사용할까요? 예: '에디터는 C:\\Program Files\\Microsoft VS Code\\Code.exe', '경로는 %USERPROFILE%\\Source'",
                AuditKind = "intent.project_open.missing_context",
                AuditPayload = new { userId = profile.Id, deviceId, editorMissing = string.IsNullOrWhiteSpace(editorPath), pathMissing = string.IsNullOrWhiteSpace(projectPath) }
            };
        }

        var args = QuoteIfNeeded(projectPath);
        var exec = await system.ExecuteAppAsync(editorPath, args, ct).ConfigureAwait(false);

        var reply = exec.Success
            ? $"프로젝트를 열었어. (deviceId={deviceId})"
            : $"프로젝트를 열지 못했어: {exec.Error}";

        return new IntentResolution
        {
            Handled = true,
            ReplyText = reply,
            Execution = exec,
            AuditKind = "intent.project_open.executed",
            AuditPayload = new { userId = profile.Id, deviceId, editorPath, projectPath, success = exec.Success, error = exec.Error }
        };
    }

    public bool TryLearnFromUserText(string userText, out Dictionary<string, string> updates)
    {
        updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        userText = (userText ?? "").Trim();
        if (userText.Length == 0) return false;

        // Very small, explicit protocol only (no guessing)
        // Examples:
        // - "에디터는 C:\\...\\Code.exe"
        // - "경로는 C:\\Source"
        var idx = userText.IndexOf("에디터", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var path = ExtractAfterAny(userText, new[] { "에디터는", "에디터:", "에디터는요" });
            path = ExpandDynamicPath(path);
            if (!string.IsNullOrWhiteSpace(path))
                updates["Preferred_Editor"] = path;
        }

        idx = userText.IndexOf("경로", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var path = ExtractAfterAny(userText, new[] { "경로는", "경로:", "경로는요" });
            path = ExpandDynamicPath(path);
            if (!string.IsNullOrWhiteSpace(path))
                updates["Default_Project_Path"] = path;
        }

        return updates.Count > 0;
    }

    private static string ExtractAfterAny(string text, string[] keys)
    {
        foreach (var k in keys)
        {
            var i = text.IndexOf(k, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            return text[(i + k.Length)..].Trim();
        }
        return "";
    }

    private static string QuoteIfNeeded(string path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0) return "";
        if (path.Contains(' ') && !(path.StartsWith('"') && path.EndsWith('"')))
            return "\"" + path + "\"";
        return path;
    }
}
