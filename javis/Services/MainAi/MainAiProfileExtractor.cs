using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services.MainAi;

public sealed class MainAiProfileExtractor
{
    public const string KeyPersonality = "user_personality";
    public const string KeyInterests = "user_interests";
    public const string KeyTechStack = "user_tech_stack";
    public const string KeyEnvPref = "user_env_pref";

    public static MainAiProfileExtractor Instance { get; } = new("http://localhost:11434", RuntimeSettings.Instance.AiModelName);

    private OllamaClient _ollama;
    private readonly string _baseUrl;
    private string _model;

    public MainAiProfileExtractor(string baseUrl, string model)
    {
        _baseUrl = baseUrl;
        _model = (model ?? "").Trim();
        if (_model.Length == 0) _model = "gemma3:4b";
        _ollama = new OllamaClient(_baseUrl, _model);
    }

    private void RefreshModelIfChanged()
    {
        var desired = (RuntimeSettings.Instance.AiModelName ?? "").Trim();
        if (desired.Length == 0) return;
        if (string.Equals(desired, _model, StringComparison.OrdinalIgnoreCase)) return;

        _model = desired;
        _ollama = new OllamaClient(_baseUrl, _model);
    }

    public async Task<Dictionary<string, string>> ExtractSafeAsync(
        string lastUserMessage,
        string existingReportText,
        CancellationToken ct)
    {
        RefreshModelIfChanged();

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
너는 '사용자 프로필 추출기'다. 목표는 사용자의 대화에서 아래 정보를 "추정 없이" 요약해 프로필 필드로 저장하는 것이다.

[스키마 고정]
- fields에는 아래 4개 키만 사용할 것. 다른 키를 만들지 마라.
  - {KeyPersonality}: 사용자의 성격 및 대화 스타일
  - {KeyInterests}: 관심 분야 및 주제
  - {KeyTechStack}: 선호하는 프로그래밍 언어, 도구, 프레임워크
  - {KeyEnvPref}: 선호하는 작업 환경(테마, 에디터 등)
- sources의 키도 위 fields 키 중 일부/전체와 동일해야 한다.

[추출 대상]
- 성격(예: 꼼꼼함, 속도 중시, 보수적/공격적 등) — 사용자가 직접 드러낸 표현만
- 관심사(도메인/업무/학습 주제)
- 기술 스택(언어/프레임워크/도구/플랫폼)
- 선호 환경(예: Windows/WSL, IDE, 배포/CI, 로컬 우선 등)

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

    private static Dictionary<string, string> NormalizeToStandardKeys(Dictionary<string, string> extracted)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (k0, v0) in extracted)
        {
            var k = (k0 ?? string.Empty).Trim();
            var v = (v0 ?? string.Empty).Trim();
            if (k.Length == 0 || v.Length == 0) continue;

            var isSource = k.EndsWith("_source", StringComparison.OrdinalIgnoreCase);
            var baseKey = isSource ? k[..^"_source".Length] : k;

            static bool KeyEq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

            string? target = null;

            if (KeyEq(baseKey, KeyPersonality) || baseKey.Contains("성격", StringComparison.OrdinalIgnoreCase) || baseKey.Contains("스타일", StringComparison.OrdinalIgnoreCase))
                target = KeyPersonality;
            else if (KeyEq(baseKey, KeyInterests) || baseKey.Contains("관심", StringComparison.OrdinalIgnoreCase) || baseKey.Contains("주제", StringComparison.OrdinalIgnoreCase))
                target = KeyInterests;
            else if (KeyEq(baseKey, KeyTechStack) || baseKey.Contains("스택", StringComparison.OrdinalIgnoreCase) || baseKey.Contains("기술", StringComparison.OrdinalIgnoreCase) || baseKey.Contains("언어", StringComparison.OrdinalIgnoreCase))
                target = KeyTechStack;
            else if (KeyEq(baseKey, KeyEnvPref) || baseKey.Contains("환경", StringComparison.OrdinalIgnoreCase) || baseKey.Contains("에디터", StringComparison.OrdinalIgnoreCase) || baseKey.Contains("IDE", StringComparison.OrdinalIgnoreCase))
                target = KeyEnvPref;

            if (target is null)
                continue;

            var outKey = isSource ? target + "_source" : target;

            if (!output.TryGetValue(outKey, out var cur) || string.IsNullOrWhiteSpace(cur))
            {
                output[outKey] = v;
            }
            else if (!cur.Contains(v, StringComparison.OrdinalIgnoreCase))
            {
                // merge with simple delimiter
                output[outKey] = (cur + " / " + v).Trim();
            }
        }

        // keep only 4 fixed keys (+ their sources)
        return output;
    }

    public async Task AnalyzeAndUpdateProfileAsync(string currentChatHistory, CancellationToken ct = default)
    {
        var history = (currentChatHistory ?? string.Empty).Trim();
        if (history.Length == 0) return;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var svc = UserProfileService.Instance;
            var profile = svc.TryGetActiveProfile();
            if (profile == null) return;

            var extracted = await ExtractSafeAsync(
                lastUserMessage: "[대화 내역]\n" + history,
                existingReportText: profile.ToReportText(includeSources: true),
                ct: timeoutCts.Token);

            if (extracted.Count == 0) return;

            extracted = MainAiFieldPolicy.Apply(extracted);
            extracted = NormalizeToStandardKeys(extracted);

            if (extracted.Count == 0) return;

            var changed = new List<string>();
            foreach (var kv in extracted)
            {
                svc.UpdateField(kv.Key, kv.Value);
                changed.Add(kv.Key);
            }

            if (changed.Count > 0)
            {
                Debug.WriteLine($"사용자 프로필 자동 업데이트 완료: {string.Join(", ", changed)}");
                try
                {
                    javis.App.Kernel?.Logger?.Log("profile.auto.updated", new { fields = changed.ToArray() });
                }
                catch { }
            }
        }
        catch
        {
            // ignore
        }
    }
}
