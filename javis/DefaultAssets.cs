using System;
using System.IO;

namespace javis;

public static class DefaultAssets
{
    public static void EnsureDefaultMathPlugin(string pluginsDir)
    {
        var path = Path.Combine(pluginsDir, "math.plugin.cs");
        if (File.Exists(path)) return;

        var code = """
using System;
using System.Text.Json;
using javis.Services;

public sealed class MathPlugin : ISkillPlugin
{
    public string Name => "Math";

    public void Register(SkillRuntime runtime)
    {
        runtime.RegisterAction("eval_math", (ctx, action) =>
        {
            if (!action.TryGetProperty("expr", out var exprEl) || exprEl.ValueKind != JsonValueKind.String)
                return "expr is required";

            var expr = exprEl.GetString() ?? "";

            try
            {
                // Basic and unsafe eval via DataTable for demo purposes.
                // (Plugins run in-process, so keep this minimal.)
                var dt = new System.Data.DataTable();
                var result = dt.Compute(expr, "");
                return result?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                return $"math error: {ex.Message}";
            }
        });
    }
}
""";

        File.WriteAllText(path, code);
    }

    public static void EnsureDefaultCalculatorSkill(string skillsDir)
    {
        var path = Path.Combine(skillsDir, "calculator.skill.json");
        if (File.Exists(path)) return;

        var json = """
{
  "id": "calculator",
  "name": "\uACC4\uC0B0\uAE30",
  "description": "\uC218\uC2DD\uC744 \uC785\uB825\uD558\uBA74 \uACC4\uC0B0\uD574\uC900\uB2E4",
  "autoRun": true,
  "triggers": [
    { "type": "regex", "pattern": "^\\s*[-+*/().0-9\\s]+\\s*$" }
  ],
  "steps": [
    { "action": { "type": "ask", "prompt": "\uC218\uC2DD \uC785\uB825:", "var": "expr" }, "saveAs": "expr" },
    { "action": { "type": "eval_math", "expr": "{{expr}}" }, "saveAs": "result" },
    { "action": { "type": "say", "text": "\uACB0\uACFC: {{result}}" } }
  ]
}
""";

        File.WriteAllText(path, json);
    }
}
