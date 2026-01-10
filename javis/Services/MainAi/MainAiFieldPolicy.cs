using System;
using System.Collections.Generic;

namespace javis.Services.MainAi;

public static class MainAiFieldPolicy
{
    public static Dictionary<string, string> Apply(Dictionary<string, string> fields)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (k0, v0) in fields)
        {
            var key = (k0 ?? string.Empty).Trim();
            var val = (v0 ?? string.Empty).Trim();
            if (key.Length == 0 || val.Length == 0) continue;

            // drop noisy/internal keys
            if (key.Equals("main_ai_model", StringComparison.OrdinalIgnoreCase))
            {
                output[key] = val;
                continue;
            }

            // PII-ish fields: keep only if clearly user-provided and not too detailed.
            // (We already instruct 'no guessing', but add guardrails.)
            if (key.Contains("주소", StringComparison.OrdinalIgnoreCase))
            {
                // If value looks like a full street address with lots of numbers, keep but truncate.
                if (val.Length > 80) val = val[..80];
            }

            // phone/email: store only minimal form, truncate
            if (key.Contains("전화", StringComparison.OrdinalIgnoreCase) || key.Contains("연락", StringComparison.OrdinalIgnoreCase))
            {
                if (val.Length > 40) val = val[..40];
            }

            if (key.Contains("이메일", StringComparison.OrdinalIgnoreCase) || key.Contains("mail", StringComparison.OrdinalIgnoreCase))
            {
                if (val.Length > 60) val = val[..60];
            }

            output[key] = val;
        }

        return output;
    }
}
