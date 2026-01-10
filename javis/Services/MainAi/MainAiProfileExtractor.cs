using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services.MainAi;

public sealed class MainAiProfileExtractor
{
    private readonly OllamaClient _ollama;

    public MainAiProfileExtractor(string baseUrl, string model)
    {
        _ollama = new OllamaClient(baseUrl, model);
    }

    public async Task<Dictionary<string, string>> ExtractSafeAsync(
        string lastUserMessage,
        string existingReportText,
        CancellationToken ct)
    {
        try
        {
            var r = await ExtractAsync(lastUserMessage, existingReportText, ct);
            return ValidateAndNormalize(r);
        }
        catch
        {
            // retry once with a shorter prompt in case model returned non-json / extra text
            try
            {
                var prompt = $$"""
JSON만 출력.
추정금지.

입력(사용자 발화):
{{lastUserMessage}}

반환(JSON):
{ "fields": { "키": "값" }, "sources": { "키": "근거" } }
""";

                var raw = await _ollama.GenerateAsync(prompt, ct);
                var json = JsonUtil.ExtractFirstJsonObject(raw);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (root.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in fieldsEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind != JsonValueKind.String) continue;
                        var key = (prop.Name ?? string.Empty).Trim();
                        var val = (prop.Value.GetString() ?? string.Empty).Trim();
                        if (key.Length == 0 || val.Length == 0) continue;
                        dict[key] = val;
                    }
                }

                if (root.TryGetProperty("sources", out var sourcesEl) && sourcesEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in sourcesEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind != JsonValueKind.String) continue;
                        var key = (prop.Name ?? string.Empty).Trim();
                        var val = (prop.Value.GetString() ?? string.Empty).Trim();
                        if (key.Length == 0 || val.Length == 0) continue;
                        dict[key + "_source"] = val;
                    }
                }

                return ValidateAndNormalize(dict);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static Dictionary<string, string> ValidateAndNormalize(Dictionary<string, string> input)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (k0, v0) in input)
        {
            var key = (k0 ?? string.Empty).Trim();
            var val = (v0 ?? string.Empty).Trim();

            if (key.Length == 0 || val.Length == 0) continue;

            // forbid reserved keys
            if (key.Equals("이름", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("생성일", StringComparison.OrdinalIgnoreCase))
                continue;

            // basic length limits (prevents garbage)
            if (key.Length > 40) key = key[..40];
            if (val.Length > 220) val = val[..220];

            // normalize whitespace
            key = string.Join(" ", key.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            val = string.Join(" ", val.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            // drop suspicious keys
            if (key.Contains("{" ) || key.Contains("}") || key.Contains("\n") || key.Contains("\r"))
                continue;

            // keep
            output[key] = val;
        }

        return output;
    }

    public async Task<Dictionary<string, string>> ExtractAsync(
        string lastUserMessage,
        string existingReportText,
        CancellationToken ct)
    {
        var prompt = $$"""
너는 '사용자 프로필 추출기'다.

[중요 규칙]
- 추정/상상 금지. 사용자가 명시적으로 말한 것만 추출한다.
- 개인 정보(주소/실명/결혼 등)는 사용자가 직접 언급한 경우에만 포함한다.
- 확실하지 않으면 아예 넣지 마라.
- 출력은 오직 JSON 객체 1개만. 설명/마크다운/코드펜스 금지.

[출력 형식]
{
  "fields": { "키": "값" },
  "sources": { "키": "근거(원문 일부)" }
}

- sources의 키는 fields의 키 중 일부/전체와 동일해야 한다.
- sources 값은 사용자의 원문(last user message)에서 발췌한 짧은 문장/구절이어야 한다.

[현재 저장된 프로필(키:값 보고서)]
{{existingReportText}}

[새 사용자 발화]
{{lastUserMessage}}

위 발화에서 새로 확정할 수 있는 프로필만 fields에 넣어라.
""";

        var raw = await _ollama.GenerateAsync(prompt, ct);
        var json = JsonUtil.ExtractFirstJsonObject(raw);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in fieldsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;

                var key = (prop.Name ?? string.Empty).Trim();
                var val = (prop.Value.GetString() ?? string.Empty).Trim();

                if (key.Length == 0 || val.Length == 0) continue;
                if (key.Equals("이름", StringComparison.OrdinalIgnoreCase) || key.Equals("ID", StringComparison.OrdinalIgnoreCase) || key.Equals("생성일", StringComparison.OrdinalIgnoreCase))
                    continue;

                dict[key] = val;
            }
        }

        if (root.TryGetProperty("sources", out var sourcesEl) && sourcesEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in sourcesEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;

                var key = (prop.Name ?? string.Empty).Trim();
                var val = (prop.Value.GetString() ?? string.Empty).Trim();
                if (key.Length == 0 || val.Length == 0) continue;

                // store as key_source
                dict[key + "_source"] = val;
            }
        }

        return dict;
    }
}
