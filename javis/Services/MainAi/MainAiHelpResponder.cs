using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services.MainAi;

public sealed class MainAiHelpResponder
{
    private readonly OllamaClient _ollama;

    public MainAiHelpResponder(string baseUrl, string model)
    {
        _ollama = new OllamaClient(baseUrl, model);
    }

    public async Task<string> AnswerAsync(string userQuestion, string codeIndexText, CancellationToken ct)
    {
        userQuestion = (userQuestion ?? "").Trim();
        if (userQuestion.Length == 0) return "";

        // Hard limit to avoid huge prompts
        if (codeIndexText.Length > 14_000)
            codeIndexText = codeIndexText.Substring(0, 14_000) + "…";

        var prompt = $$"""
너는 WPF 데스크톱 앱 'JARVIS'의 내장 도움말 AI다.

규칙:
- 한국어로, 짧고 정확하게.
- 모르면 모른다고 말하고, 어디를 보면 되는지(파일/화면/메뉴)를 안내.
- 기능 설명/사용법 위주.
- 아래 [CODE INDEX]에 있는 파일/클래스/키워드만 근거로 말해라.
- 추정 금지.

[사용자 질문]
{{userQuestion}}

[CODE INDEX]
{{codeIndexText}}

출력 형식:
- 3~8줄로 요약
- 관련 파일/화면이 있으면 마지막 줄에: "관련: ..." 로 경로를 나열
""";

        var raw = await _ollama.GenerateAsync(prompt, ct);
        return (raw ?? "").Trim();
    }
}
