using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace javis.Services;

public interface ISkillPlugin
{
    string Name { get; }
    void Register(SkillRuntime runtime);
}

public sealed class SkillRuntime
{
    private readonly Dictionary<string, Func<SkillContext, JsonElement, string>> _actions = new();

    public SkillRuntime()
    {
        RegisterAction("ask", HandleAsk);
    }

    private static string HandleAsk(SkillContext ctx, JsonElement action)
    {
        // Expected fields (skill JSON): var, prompt
        var varName = action.TryGetProperty("var", out var vEl) ? (vEl.GetString() ?? "") : "";

        // ? already have value => skip ask
        if (!string.IsNullOrWhiteSpace(varName) &&
            ctx.Vars.TryGetValue(varName, out var existing) &&
            existing is not null &&
            !string.IsNullOrWhiteSpace(existing.ToString()))
        {
            return existing.ToString() ?? "";
        }

        var prompt = action.TryGetProperty("prompt", out var pEl) ? (pEl.GetString() ?? "") : "";

        // No UI prompt system in this runtime; fail fast with a clear message.
        if (string.IsNullOrWhiteSpace(prompt))
            prompt = !string.IsNullOrWhiteSpace(varName) ? $"값 입력 필요: {varName}" : "값 입력 필요";

        // If no prefilled var is available, return a message so the user can supply vars.
        return $"(ask) {prompt}";
    }

    public void RegisterAction(string actionType, Func<SkillContext, JsonElement, string> handler)
        => _actions[actionType] = handler;

    public bool TryGetAction(string actionType, out Func<SkillContext, JsonElement, string> handler)
        => _actions.TryGetValue(actionType, out handler!);

    public IEnumerable<string> ActionTypes => _actions.Keys.OrderBy(x => x);
}

public sealed class SkillContext
{
    public string DataDir { get; }
    public string MemoFile { get; }
    public string TodoFile { get; }

    public Dictionary<string, object> Vars { get; }

    public SkillContext(string dataDir, string memoFile, string todoFile, Dictionary<string, object>? vars = null)
    {
        DataDir = dataDir;
        MemoFile = memoFile;
        TodoFile = todoFile;
        Vars = vars ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }
}
