using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services;

public sealed class SftDatasetExporter
{
    private readonly string _dataDir;
    private readonly PluginHost _host;

    public SftDatasetExporter(PluginHost host)
    {
        _host = host;
        _dataDir = host.DataDir;
    }

    /// <summary>
    /// ChatML JSONL: {"messages":[{"role":"system","content":"..."}, ...]}
    /// </summary>
    public async Task<int> ExportChatMlJsonlAsync(
        string outputPath,
        bool includeCanon = true,
        bool includeSoloNotes = true,
        int maxCanonItems = 800,
        int maxNotesItems = 400,
        CancellationToken ct = default)
    {
        var samples = new List<ChatMlSample>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Canon → Q/A 템플릿으로 변환
        if (includeCanon)
        {
            var canonItems = ReadCanonItems(_host.Canon.CanonPath, maxCanonItems);
            foreach (var it in canonItems)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(it.title) || string.IsNullOrWhiteSpace(it.body)) continue;

                foreach (var (q, a) in BuildCanonQaVariants(it))
                {
                    var key = Sha1($"{q}\n{a}");
                    if (!seen.Add(key)) continue;

                    samples.Add(new ChatMlSample(new[]
                    {
                        Msg("system", BuildSystemForTraining()),
                        Msg("user", q),
                        Msg("assistant", a)
                    }));
                }
            }
        }

        // 2) SOLO 노트 → “요약/정리/다음 행동” 스타일 학습 샘플로 변환
        if (includeSoloNotes)
        {
            var noteItems = ReadRecentSoloNotes(maxDays: 7, maxItems: maxNotesItems);
            foreach (var it in noteItems)
            {
                ct.ThrowIfCancellationRequested();

                // 노트 내용이 너무 짧으면 스킵
                if (it.Body.Length < 40) continue;

                foreach (var (q, a) in BuildNoteStyleVariants(it))
                {
                    var key = Sha1($"{q}\n{a}");
                    if (!seen.Add(key)) continue;

                    samples.Add(new ChatMlSample(new[]
                    {
                        Msg("system", BuildSystemForTraining()),
                        Msg("user", q),
                        Msg("assistant", a)
                    }));
                }
            }
        }

        // 3) 파일로 저장 (jsonl)
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var s in samples)
        {
            ct.ThrowIfCancellationRequested();
            var line = JsonSerializer.Serialize(s);
            await sw.WriteLineAsync(line);
        }

        return samples.Count;
    }

    // ===== Canon Read =====

    private static List<CanonItemLite> ReadCanonItems(string canonPath, int maxItems)
    {
        var list = new List<CanonItemLite>();
        if (!File.Exists(canonPath)) return list;

        foreach (var line in File.ReadLines(canonPath))
        {
            try
            {
                var it = JsonSerializer.Deserialize<CanonItemLite>(line);
                if (it is null) continue;
                if (string.IsNullOrWhiteSpace(it.title) || string.IsNullOrWhiteSpace(it.body)) continue;
                list.Add(it);
                if (list.Count >= maxItems) break;
            }
            catch { }
        }
        return list;
    }

    private static IEnumerable<(string q, string a)> BuildCanonQaVariants(CanonItemLite it)
    {
        // “네 지식이 일반지식이 되게” 하려면 가장 단순하고 강한 방식은:
        // - title 중심 질문 → body 답변
        // - 규칙/정의/원칙 프롬프트로 body 답변
        var title = it.title.Trim();
        var body = it.body.Trim();

        var tags = it.tags is { Length: > 0 } ? $"(태그: {string.Join(", ", it.tags.Take(8))})" : "";

        yield return ($"{title}에 대해 설명해줘. {tags}".Trim(), body);
        yield return ($"{title}의 핵심 규칙/정의를 간단히 정리해줘. {tags}".Trim(), body);

        // body가 길면 “요약” 형태도 추가(학습 안정성)
        if (body.Length > 500)
        {
            yield return ($"{title} 내용을 5줄 이내로 요약해줘. {tags}".Trim(), MakeShortSummary(body, 5));
        }
    }

    // ===== SOLO Notes Read =====

    private List<NoteLite> ReadRecentSoloNotes(int maxDays, int maxItems)
    {
        var list = new List<NoteLite>();

        var dir = _host.SoloNotes.Dir;
        if (!Directory.Exists(dir)) return list;

        var files = Directory.GetFiles(dir, "notes-*.jsonl")
                             .OrderByDescending(x => x)
                             .ToList();

        var cutoff = DateTime.Today.AddDays(-maxDays);

        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f); // notes-YYYY-MM-DD
            if (TryParseDateFromNotesFile(name, out var d))
            {
                if (d < cutoff) break;
            }

            foreach (var line in File.ReadLines(f).Reverse())
            {
                if (list.Count >= maxItems) return list;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var kind = root.TryGetProperty("kind", out var kEl) ? (kEl.GetString() ?? "") : "";
                    if (kind is not ("note" or "observation" or "memory_brew")) continue;

                    if (!root.TryGetProperty("data", out var data)) continue;

                    var title = data.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "") : "";
                    var body = data.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(body)) continue;

                    var tags = ReadStringArray(data, "tags", take: 10);
                    var qs = ReadStringArray(data, "questions", take: 10);

                    list.Add(new NoteLite(title.Trim(), body.Trim(), tags, qs));
                }
                catch { }
            }
        }

        return list;
    }

    private static IEnumerable<(string q, string a)> BuildNoteStyleVariants(NoteLite it)
    {
        // 노트 자체를 “좋은 답변 패턴”으로 학습시키는 샘플
        // (사용자가 ‘상황/자료’를 주면 Jarvis가 관찰/연결/다음 질문을 만드는 스타일)
        var context = $"제목: {it.Title}\n내용:\n{it.Body}";
        if (it.Tags.Length > 0) context += $"\n태그: {string.Join(", ", it.Tags.Take(8))}";
        if (it.Questions.Length > 0) context += $"\n질문: {string.Join(" | ", it.Questions.Take(5))}";

        yield return (
            "아래 기록을 바탕으로 관찰을 3~6개로 정리해줘.\n\n" + context,
            BulletizeFromBody(it.Body, maxBullets: 6)
        );

        yield return (
            "아래 기록에서 다음에 이어갈 질문 2~5개를 만들어줘.\n\n" + context,
            MakeQuestionsFromNote(it)
        );
    }

    // ===== System prompt used in training samples =====

    private string BuildSystemForTraining()
    {
        // 페르소나의 핵심(너가 파일로 관리하는 core)만 넣어 “Jarvis 스타일”을 학습
        // 너무 길면 학습 효율 떨어져서 core를 적당히 짧게 유지하는 걸 추천
        var core = _host.Persona.CoreText?.Trim() ?? "";
        if (core.Length > 1200) core = core.Substring(0, 1200) + "…";
        return core;
    }

    // ===== Helpers =====

    private static ChatMlMessage Msg(string role, string content) => new(role, content);

    private static string Sha1(string s)
    {
        using var sha1 = SHA1.Create();
        var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool TryParseDateFromNotesFile(string name, out DateTime date)
    {
        // name: notes-YYYY-MM-DD
        date = default;
        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return false;

        var y = parts[^3];
        var m = parts[^2];
        var d = parts[^1];
        return DateTime.TryParse($"{y}-{m}-{d}", out date);
    }

    private static string[] ReadStringArray(JsonElement root, string name, int take)
    {
        var list = new List<string>();
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return Array.Empty<string>();

        foreach (var it in el.EnumerateArray())
        {
            var s = it.ValueKind == JsonValueKind.String ? (it.GetString() ?? "") : it.ToString();
            s = (s ?? "").Trim();
            if (s.Length == 0) continue;
            list.Add(s);
            if (list.Count >= take) break;
        }
        return list.ToArray();
    }

    private static string MakeShortSummary(string body, int maxLines)
    {
        // 간단 요약(학습 데이터 생성용): 문장 몇 개만 잘라서 라인으로
        var sentences = body.Replace("\r", " ").Split('.', StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => x.Length > 0)
                            .Take(maxLines)
                            .ToList();
        if (sentences.Count == 0) return body.Length <= 400 ? body : body.Substring(0, 400) + "…";
        return string.Join(". ", sentences) + ".";
    }

    private static string BulletizeFromBody(string body, int maxBullets)
    {
        // 아주 단순한 bulletizer: 줄/문장 기반
        var lines = body.Replace("\r", "")
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .ToList();

        if (lines.Count == 0)
        {
            var s = MakeShortSummary(body, maxBullets);
            return "- " + s.Replace("\n", "\n- ");
        }

        // 너무 긴 줄은 잘라서
        var bullets = lines.Select(x => x.Length > 180 ? x.Substring(0, 180) + "…" : x)
                           .Take(maxBullets)
                           .ToList();

        return "- " + string.Join("\n- ", bullets);
    }

    private static string MakeQuestionsFromNote(NoteLite it)
    {
        if (it.Questions.Length > 0)
            return "- " + string.Join("\n- ", it.Questions.Take(5));

        // 질문이 없으면 본문에서 ‘다음 행동’ 질문 몇 개 템플릿 생성
        var baseQ = it.Title.Length > 0 ? it.Title : "이 기록";
        return "- " + string.Join("\n- ", new[]
        {
            $"{baseQ}에서 다음에 확인해야 할 사실은 뭐야?",
            $"{baseQ}을 실행 가능한 작업으로 쪼개면 무엇이 남아?",
            $"{baseQ}과 가장 충돌할 수 있는 가정은 뭐야?"
        });
    }

    // ===== Lite models for file parsing =====

    private sealed record CanonItemLite(string id, string ts, string kind, string title, string body, string[] tags);

    private sealed record NoteLite(string Title, string Body, string[] Tags, string[] Questions);

    private sealed record ChatMlSample(ChatMlMessage[] messages);

    private sealed record ChatMlMessage(string role, string content);
}
