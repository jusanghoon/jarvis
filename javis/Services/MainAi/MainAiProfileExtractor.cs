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

    public async Task<Dictionary<string, string>> ExtractAsync(
        string lastUserMessage,
        string existingReportText,
        CancellationToken ct)
    {
        var prompt = $"""
너는 '사용자 프로필 추출기'다.

[중요 규칙]
- 추정/상상 금지. 사용자가 명시적으로 말한 것만 추출한다.
- 개인 정보(주소/실명/결혼 등)는 사용자가 직접 언급한 경우에만 포함한다.
- 확실하지 않으면 아예 넣지 마라.
- 출력은 오직 JSON 객체 1개만. 설명/마크다운/코드펜스 금지.

[출력 형식]
{{
  \"fields\": {{ \"키\": \"값\", ... }}
}}

[현재 저장된 프로필(키:값 보고서)]
{existingReportText}

[새 사용자 발화]
{lastUserMessage}

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

        return dict;
    }
}
