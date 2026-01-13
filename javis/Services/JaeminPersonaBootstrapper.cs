using System;
using System.IO;
using System.Text;

namespace javis.Services;

public static class JaeminPersonaBootstrapper
{
    public static void EnsureJaeminPersonaFromPdfIfPresent(string activeUserDataDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(activeUserDataDir)) return;

            var personaDir = Path.Combine(activeUserDataDir, "persona");
            Directory.CreateDirectory(personaDir);

            // Diagnostic marker (helps confirm bootstrap ran)
            try
            {
                File.WriteAllText(
                    Path.Combine(personaDir, "bootstrap_jaemin.v1"),
                    DateTimeOffset.Now.ToString("O"),
                    new UTF8Encoding(true));
            }
            catch { }

            var corePath = Path.Combine(personaDir, "core.txt");
            var coreSrcPath = Path.Combine(personaDir, "core_source.txt");
            var chatPath = Path.Combine(personaDir, "chat_overlay.txt");
            var soloPath = Path.Combine(personaDir, "solo_overlay.txt");

            if (File.Exists(corePath) && File.Exists(chatPath) && File.Exists(soloPath))
                return;

            var pdfPath = ResolveBundledPdfPath();
            if (pdfPath == null) return;

            var raw = PersonaPdfImporter.TryExtractText(pdfPath);
            if (string.IsNullOrWhiteSpace(raw)) return;

            // Always preserve extracted source (append-only style not required here; just keep first snapshot)
            try
            {
                if (!File.Exists(coreSrcPath))
                    File.WriteAllText(coreSrcPath, raw.Trim(), new UTF8Encoding(true));
            }
            catch { }

            // Style B: strict protocol-driven persona.
            var core = BuildCoreSummary();
            var chat = BuildChatOverlay();
            var solo = BuildSoloOverlay();

            // Write only missing files to avoid overwriting user edits.
            if (!File.Exists(corePath)) File.WriteAllText(corePath, core, new UTF8Encoding(true));
            if (!File.Exists(chatPath)) File.WriteAllText(chatPath, chat, new UTF8Encoding(true));
            if (!File.Exists(soloPath)) File.WriteAllText(soloPath, solo, new UTF8Encoding(true));
        }
        catch
        {
            // never crash app due to bootstrap
        }
    }

    private static string? ResolveBundledPdfPath()
    {
        try
        {
            // App base directory is usually ...\bin\Debug\net10.0-windows\
            // Walk up a few levels to find repo root that contains `javis` folder.
            var root = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var parent = Directory.GetParent(root);
                if (parent is null) break;
                root = parent.FullName;

                var candidate = Path.Combine(root, "javis", "Assets", "Persona", "jaemin_persona.pdf");
                if (File.Exists(candidate))
                    return candidate;
            }

            // Fallback: local relative (if running from repo root)
            var rel = Path.Combine("javis", "Assets", "Persona", "jaemin_persona.pdf");
            if (File.Exists(rel)) return Path.GetFullPath(rel);
        }
        catch { }

        return null;
    }

    private static string BuildCoreSummary()
    {
        return """
[IDENTITY]
- 너의 이름은 '재민'이다.
- 너는 사용자의 개인비서/아키텍트 파트너로서, 규칙과 프로토콜 기반으로 판단한다.

[OPERATING PRINCIPLES]
- 추정 금지: 사용자 제공 사실/기록/파일만 근거로 답한다.
- 불확실하면 질문: 결정을 위한 최소 질문(1~3개)을 먼저 한다.
- 구조화: (1)요약 (2)근거 (3)결정/제안 (4)다음 행동.
- 충돌 처리: 기록과 입력이 충돌하면 충돌 지점을 명시하고 선택지를 병렬 제시한다.
- 기록 우선: 결정/원칙/정의는 재사용 가능한 형태로 정리해 SSOT에 축적한다.

[STRICT STYLE]
- 한국어, 짧고 정확하게.
- 군더더기 인사/메타 발언 금지.
""";
    }

    private static string BuildChatOverlay() =>
        """
[CHAT MODE]
- 톤: 짧고 단호. 규칙/근거 중심.
- 답변 형식: 필요할 때만 번호 목록.
- 모르면 '모름' + 확인해야 할 위치(파일/화면/설정)를 말한다.
- 요구사항/목표/제약이 불명확하면 먼저 질문한다.
""";

    private static string BuildSoloOverlay() =>
        """
[SOLO MODE]
- 출력은 반드시 JSON 하나만.
- 의도(intent)를 정확히 선택하고, 불필요한 행동(save_note/create_skill)을 남발하지 않는다.
""";
}
