using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace javis.Services;

public sealed record BuildOut(
    bool PluginRequired,
    string? PluginFilename,
    string? PluginCode,
    string SkillFilename,
    string SkillSpecJson);

public static class SkillBuilder
{
    public static async Task<BuildOut> BuildAsync(string requirement, List<string> actionTypes, string apiNamespace)
    {
        var ollama = new OllamaClient("http://localhost:11434", "qwen3:4b");
        var actions = string.Join(", ", actionTypes);

        var prompt = $"""
\uB108\uB294 WPF Jarvis\uC758 \uC2A4\uD0AC/\uD50C\uB7EC\uADF8\uC778 \uC0DD\uC131\uAE30\uB2E4.
\uBC18\uB4DC\uC2DC JSON \uAC1D\uCCB4 \uD558\uB098\uB9CC \uCD9C\uB825\uD558\uB77C(\uC124\uBA85/\uB9C8\uD06C\uB2E4\uC6B4/\uCF54\uB4DC\uD39C\uC2A4 \uAE08\uC9C0).

\uD604\uC7AC \uC0AC\uC6A9 \uAC00\uB2A5\uD55C action.type:
{actions}

\uD50C\uB7EC\uADF8\uC778\uC5D0\uC11C \uC0AC\uC6A9\uD560 API \uB124\uC784\uC2A4\uD398\uC774\uC2A4:
{apiNamespace}
(\uD50C\uB7EC\uADF8\uC778 \uCF54\uB4DC \uB9E8 \uC704\uC5D0 using {apiNamespace}; \uB97C \uD3EC\uD568\uD558\uB77C)

\uBC18\uD658 JSON \uD544\uB4DC:
- plugin_required (bool)
- plugin_filename (string or null)
- plugin_code (string or null)
- skill_filename (string)
- skill_spec (object)

Skill JSON\uC740 steps \uBC30\uC5F4\uC744 \uAC16\uB294\uB2E4:
- step.action.type \uD544\uC218
- step.saveAs\uB294 \uC120\uD0DD

\uC694\uAD6C\uC0AC\uD56D:
{requirement}
""";

        var raw = await ollama.GenerateAsync(prompt);
        var json = ExtractFirstJsonObject(raw);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var pr = root.GetProperty("plugin_required").GetBoolean();
        var pf = root.TryGetProperty("plugin_filename", out var pfEl) ? pfEl.GetString() : null;
        var pc = root.TryGetProperty("plugin_code", out var pcEl) ? pcEl.GetString() : null;

        var sf = root.GetProperty("skill_filename").GetString()!;
        var spec = root.GetProperty("skill_spec").GetRawText();

        return new BuildOut(
            pr,
            pr ? Sanitize(pf!, ".plugin.cs") : null,
            pr ? pc : null,
            Sanitize(sf, ".skill.json"),
            JsonNode.Parse(spec)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        );
    }

    private static string ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        if (start < 0) throw new Exception("JSON \uC2DC\uC791 '{' \uC5C6\uC74C");

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

        throw new Exception("JSON \uCD94\uCD9C \uC2E4\uD328");
    }

    private static string Sanitize(string file, string suffix)
    {
        var name = Path.GetFileName(file).Trim();
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name) + suffix;

        return new string(name.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? char.ToLowerInvariant(ch) : '_'
        ).ToArray());
    }
}
