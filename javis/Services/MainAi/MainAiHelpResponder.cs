using System;
using System.Threading;
using System.Threading.Tasks;
using javis.Services;

namespace javis.Services.MainAi;

public sealed class MainAiHelpResponder
{
    private OllamaClient _ollama;
    private readonly string _baseUrl;
    private string _model;

    public MainAiHelpResponder(string baseUrl, string model)
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

    public async Task<string> AnswerAsync(string userQuestion, string codeIndexText, CancellationToken ct)
    {
        RefreshModelIfChanged();

        userQuestion = (userQuestion ?? "").Trim();
        if (userQuestion.Length == 0) return "";

        // Hard limit to avoid huge prompts
        codeIndexText = (codeIndexText ?? "").Trim();
        if (codeIndexText.Length > 5_000)
            codeIndexText = codeIndexText.Substring(0, 5_000) + "…";

        var aiName = (RuntimeSettings.Instance.MainAiName ?? "").Trim();
        if (aiName.Length == 0) aiName = "JARVIS";

        var prompt = $$"""
너는 WPF 데스크톱 앱 '{{aiName}}'의 Central AI Controller (System Operations Manager)다.

추가 역할(리팩토링/구조 최적화):
- 너는 AnalysisReport 및 코드 스니펫을 기반으로 C# 및 WPF 프로젝트의 아키텍처를 최적화하는 Architectural Optimizer다.

리팩토링 원칙:
- Single Responsibility: 한 클래스/파일이 너무 많은 일을 하면(예: 1000라인 이상) 기능별로 분리 제안.
- MVVM Pure: View에 비즈니스 로직이 있으면 ViewModel/Service로 이관 제안.
- Dead Code: 참조되지 않는 리소스/메서드/파일은 삭제 또는 격리 제안.

리팩토링 제안 출력 규칙:
- 리팩토링/구조 변경을 제안할 때는 반드시 아래 순서로 먼저 작성한다.
  [REASON]
  - 왜 고쳐야 하는가(유지보수/테스트/결합도/성능/안정성 관점)
  [PLAN]
  - 어떤 순서로 어떤 파일을 어떻게 쪼갤 것인가(구체적 대상/새 파일명/책임 분리)

목표:
- 사용자의 요청을 이해하고, 가능한 경우 앱을 "직접" 조작하도록 지시(JSON intent)한다.

중요 규칙:
- 반드시 JSON 하나만 출력.
- 한국어.
- 사용자가 "코드 좀 봐줘", "원인 찾아봐", "어디가 이상해" 같이 디버깅/원인분석을 요구하면:
  1) 먼저 관련 코드 파일을 read_code로 열어 확인하고
  2) 그 다음 턴에서 분석/설명을 한다.

가능한 intent:
- navigate: 특정 화면으로 이동
  - target: home|todos|skills|settings|updates
- action: 화면 이동 외의 단순 액션
  - name: open_todos_today|open_todos_tomorrow|open_todos_date|open_settings_updates|open_user_profiles
  - date: open_todos_date일 때만 ("yyyy-MM-dd" 또는 "오늘|내일|모레")
- read_code: 소스 코드(파일) 내용을 읽어서 보여달라는 요청 (분석을 위해 먼저 열어볼 때도 사용)
  - path: 상대 경로(예: "javis/ViewModels/MainAiWidgetViewModel.cs")
  - hint: 사용자가 말한 기능/파일/클래스 힌트(선택)
  - related: 추가로 함께 확인하면 좋은 파일 경로들(선택, 최대 3개)
- say: 안내 문구만 출력
  - text: 1~8줄 안내

[APP INDEX]에는 request_log(reasoning_history를 포함할 수 있음) 및 tag_index가 포함될 수 있다.
- tag_index의 [date_time], [calendar], [todos] 같은 섹션에서 파일 경로를 골라 path에 넣어라.
- 가능하면 해당 tag의 top_files를 참고해 related에도 1~3개를 채워라.
- path를 고룰 수 없으면 hint에 기능 키워드(예: "date", "calendar", "todo")를 넣고 read_code를 시도해라.

사용자 질문: {{userQuestion}}

[APP INDEX]
{{codeIndexText}}

JSON 스키마:
{ "intent": "navigate|action|read_code|say", "target": "home|todos|skills|settings|updates", "name": "...", "date": "...", "path": "...", "hint": "...", "related": ["..."], "text": "..." }

출력 예시:
{ "intent": "action", "name": "open_todos_tomorrow", "text": "내일 할 일을 열어줄게." }
{ "intent": "read_code", "hint": "date_time", "text": "날짜 처리 코드를 먼저 확인할게." }
""";

        var raw = await _ollama.GenerateAsync(prompt, ct);
        return (raw ?? "").Trim();
    }

    public async Task<string> AnalyzeWithCodeAsync(string userQuestion, string relPath, string codeSnippet, CancellationToken ct)
    {
        userQuestion = (userQuestion ?? "").Trim();
        relPath = (relPath ?? "").Trim();
        codeSnippet = (codeSnippet ?? "").Trim();

        if (userQuestion.Length == 0) return "";

        // Avoid massive follow-up prompts
        if (codeSnippet.Length > 18_000)
            codeSnippet = codeSnippet.Substring(0, 18_000) + "\n\n…(truncated)";

        var aiName = (RuntimeSettings.Instance.MainAiName ?? "").Trim();
        if (aiName.Length == 0) aiName = "JARVIS";

        var prompt = $$"""
너는 WPF 데스크톱 앱 '{{aiName}}'의 Central AI Controller (System Operations Manager)다.

사용자가 기능 이상/버그를 제보했다. 아래 코드를 읽고, 반드시 "자연어"로만 답해라.
- JSON 출력 금지
- 코드 블록은 필요할 때만 짧게
- 결론(원인 후보) -> 근거(코드 위치/행은 모르면 파일/구문으로) -> 해결책(구체적 수정 포인트) 순서로

사용자 질문:
{{userQuestion}}

주요 파일: {{relPath}}

[CODE]
{{codeSnippet}}
""";

        var raw = await _ollama.GenerateAsync(prompt, ct);
        return (raw ?? "").Trim();
    }

    public async Task<string> PickRelevantFilesAsync(string userQuestion, System.Collections.Generic.IReadOnlyList<string> candidatePaths, string bundledSnippets, CancellationToken ct)
    {
        userQuestion = (userQuestion ?? "").Trim();
        bundledSnippets = (bundledSnippets ?? "").Trim();
        if (userQuestion.Length == 0) return "";

        if (bundledSnippets.Length > 22_000)
            bundledSnippets = bundledSnippets.Substring(0, 22_000) + "\n\n…(truncated)";

        var aiName = (RuntimeSettings.Instance.MainAiName ?? "").Trim();
        if (aiName.Length == 0) aiName = "JARVIS";

        var list = string.Join("\n", candidatePaths ?? Array.Empty<string>());

        var prompt = $$"""
너는 WPF 데스크톱 앱 '{{aiName}}'의 Central AI Controller (System Operations Manager)다.

사용자 질문에 답하기 위해, 아래 후보 파일들 중에서 "우선 확인할 핵심 파일" 1~3개를 골라라.

규칙:
- 반드시 JSON 하나만 출력
- 스키마: { "pick": ["path1", "path2"] }
- pick 배열에는 후보 목록에 있는 경로만 넣어라

사용자 질문:
{{userQuestion}}

후보 파일 목록:
{{list}}

후보 코드 스니펫 묶음:
{{bundledSnippets}}
""";

        var raw = await _ollama.GenerateAsync(prompt, ct);
        return (raw ?? "").Trim();
    }
}
