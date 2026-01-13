using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace javis.Services.MainAi;

public static class MainAiCodeIndex
{
    public static string BuildIndexText(string solutionRoot, int maxFiles = 220)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(solutionRoot) || !Directory.Exists(solutionRoot))
                return "(code index unavailable)";

            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".xaml"
            };

            var files = Directory.EnumerateFiles(solutionRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f)))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .Take(maxFiles)
                .ToList();

            // Instead of listing file paths (privacy + noise), summarize by building
            // a small "tag -> file" map for key features.
            var pages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var viewModels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var services = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            var tagToFiles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            static void AddTag(Dictionary<string, SortedSet<string>> map, string tag, string rel)
            {
                if (!map.TryGetValue(tag, out var set))
                {
                    set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[tag] = set;
                }
                set.Add(rel);
            }

            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(solutionRoot, f).Replace('\\', '/');
                var name = Path.GetFileNameWithoutExtension(rel);

                if (rel.StartsWith("javis/Pages/", StringComparison.OrdinalIgnoreCase))
                    pages.Add(name);
                else if (rel.StartsWith("javis/ViewModels/", StringComparison.OrdinalIgnoreCase))
                    viewModels.Add(name);
                else if (rel.StartsWith("javis/Services/", StringComparison.OrdinalIgnoreCase))
                    services.Add(name);

                foreach (var t in InferTags(rel))
                    AddTag(tagToFiles, t, rel);
            }

            var sb = new StringBuilder();
            sb.AppendLine("features:");
            sb.AppendLine("- navigation: Home / Chat / Todos / Skills / Settings");
            sb.AppendLine("- right panel: Main AI widget (help)");
            sb.AppendLine("- calendar: Home page calendar + upcoming todos");
            sb.AppendLine("- updates: suggestions + release notes");
            sb.AppendLine();

            sb.AppendLine("pages:");
            foreach (var p in pages.Take(50)) sb.AppendLine("- " + p);

            sb.AppendLine();
            sb.AppendLine("viewmodels:");
            foreach (var vm in viewModels.Take(60)) sb.AppendLine("- " + vm);

            sb.AppendLine();
            sb.AppendLine("services:");
            foreach (var s in services.Take(60)) sb.AppendLine("- " + s);

            sb.AppendLine();
            sb.AppendLine("tag_index:");
            sb.AppendLine("- 목적: 기능 질문(예: 날짜/캘린더/투두/채팅) 시 관련 코드 파일 경로를 고르기 위한 힌트");
            sb.AppendLine("- 사용법: tag가 맞으면 그 아래 file path 중 하나를 path에 넣어서 read_code intent로 요청");
            sb.AppendLine("- 참고: 각 tag에는 top_files(우선 확인)도 함께 제공됨");

            foreach (var kv in tagToFiles.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (kv.Value.Count == 0) continue;
                sb.AppendLine();
                sb.AppendLine($"[{kv.Key}]");

                sb.AppendLine("top_files:");
                foreach (var rel in kv.Value.Take(5))
                    sb.AppendLine("- " + rel);

                sb.AppendLine("files:");
                foreach (var rel in kv.Value.Take(25))
                    sb.AppendLine("- " + rel);
            }

            return sb.ToString();
        }
        catch
        {
            return "(code index build failed)";
        }
    }

    public static IReadOnlyList<string> InferTagsFromQuestionPublic(string userQuestion)
        => InferTagsFromQuestion(userQuestion);

    public static IReadOnlyList<string> SuggestRelatedPaths(string solutionRoot, string userQuestion, int maxPaths = 3)
    {
        try
        {
            userQuestion = (userQuestion ?? "").Trim();
            if (userQuestion.Length == 0) return Array.Empty<string>();

            var tags = InferTagsFromQuestion(userQuestion).ToArray();
            if (tags.Length == 0) return Array.Empty<string>();

            return SuggestRelatedPaths(solutionRoot, tags, boostsByTag: null, maxPaths: maxPaths);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> SuggestRelatedPaths(
        string solutionRoot,
        IReadOnlyList<string> tags,
        Func<string, IReadOnlyDictionary<string, int>>? boostsByTag,
        int maxPaths = 8)
    {
        try
        {
            if (tags is null || tags.Count == 0) return Array.Empty<string>();

            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".xaml" };

            // We keep this bounded: enumerate typical app folders first.
            var roots = new[]
            {
                Path.Combine(solutionRoot, "javis"),
                Path.Combine(solutionRoot, "Jarvis.Core"),
                Path.Combine(solutionRoot, "Jarvis.Abstractions"),
                Path.Combine(solutionRoot, "Jarvis.Modules.Vault"),
            };

            var candidates = new List<string>(1024);
            foreach (var r in roots)
            {
                if (!Directory.Exists(r)) continue;
                try
                {
                    candidates.AddRange(Directory.EnumerateFiles(r, "*.*", SearchOption.AllDirectories)
                        .Where(f => exts.Contains(Path.GetExtension(f)))
                        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        .Take(8000));
                }
                catch { }
            }

            // Preload boosts
            var boostMaps = new List<IReadOnlyDictionary<string, int>>(tags.Count);
            if (boostsByTag != null)
            {
                foreach (var t in tags)
                    boostMaps.Add(boostsByTag(t));
            }

            var scored = candidates
                .Select(f =>
                {
                    var rel = Path.GetRelativePath(solutionRoot, f).Replace('\\', '/');
                    var fileTags = InferTags(rel);
                    var baseScore = fileTags.Count(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase));

                    var boost = 0;
                    if (boostMaps.Count > 0)
                    {
                        foreach (var map in boostMaps)
                        {
                            if (map.TryGetValue(rel, out var b)) boost += b;
                        }
                    }

                    return (rel, score: baseScore * 10 + boost);
                })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.rel, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.rel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, maxPaths))
                .ToList();

            return scored;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> InferTagsFromQuestion(string userQuestion)
    {
        var q = (userQuestion ?? "").ToLowerInvariant();
        var tags = new List<string>(8);

        if (q.Contains("날짜") || q.Contains("시간") || q.Contains("요일") || q.Contains("캘린더") || q.Contains("달력") || q.Contains("date") || q.Contains("time"))
            tags.Add("date_time");

        if (q.Contains("캘린더") || q.Contains("달력") || q.Contains("calendar"))
            tags.Add("calendar");

        if (q.Contains("할일") || q.Contains("할 일") || q.Contains("투두") || q.Contains("todo"))
            tags.Add("todos");

        if (q.Contains("채팅") || q.Contains("대화") || q.Contains("chat"))
            tags.Add("chat");

        if (q.Contains("업데이트") || q.Contains("릴리즈") || q.Contains("release") || q.Contains("update"))
            tags.Add("updates");

        if (q.Contains("스킬") || q.Contains("skill"))
            tags.Add("skills");

        if (q.Contains("지도") || q.Contains("맵") || q.Contains("map"))
            tags.Add("maps");

        if (q.Contains("설정") || q.Contains("settings"))
            tags.Add("settings");

        if (q.Contains("vault") || q.Contains("금고"))
            tags.Add("vault");

        if (q.Contains("ai") || q.Contains("재민") || q.Contains("ollama"))
            tags.Add("ai_main");

        return tags;
    }

    private static IReadOnlyList<string> InferTags(string relPath)
    {
        // simple, cheap heuristics: folder names + file name tokens
        var rel = (relPath ?? "").Replace('\\', '/');
        var lower = rel.ToLowerInvariant();

        var fileName = Path.GetFileNameWithoutExtension(rel);
        var fn = (fileName ?? "").ToLowerInvariant();

        var tags = new List<string>(8);

        if (lower.Contains("/pages/")) tags.Add("ui_pages");
        if (lower.Contains("/viewmodels/")) tags.Add("viewmodels");
        if (lower.Contains("/services/")) tags.Add("services");
        if (lower.Contains("/converters/")) tags.Add("converters");
        if (lower.Contains("/models/")) tags.Add("models");

        // feature-ish tokens
        if (lower.Contains("calendar") || fn.Contains("calendar", StringComparison.Ordinal))
        {
            tags.Add("calendar");
            tags.Add("date_time");
        }

        if (lower.Contains("date") || fn.Contains("date", StringComparison.Ordinal) || fn.Contains("kst", StringComparison.Ordinal))
            tags.Add("date_time");

        if (lower.Contains("todo") || fn.Contains("todo", StringComparison.Ordinal))
            tags.Add("todos");

        if (lower.Contains("chat") || fn.Contains("chat", StringComparison.Ordinal))
            tags.Add("chat");

        if (lower.Contains("update") || fn.Contains("update", StringComparison.Ordinal) || lower.Contains("release"))
            tags.Add("updates");

        if (lower.Contains("skill") || fn.Contains("skill", StringComparison.Ordinal))
            tags.Add("skills");

        if (lower.Contains("map") || fn.Contains("map", StringComparison.Ordinal))
            tags.Add("maps");

        if (lower.Contains("persona") || fn.Contains("persona", StringComparison.Ordinal) || lower.Contains("profile"))
            tags.Add("persona_profiles");

        if (lower.Contains("vault") || fn.Contains("vault", StringComparison.Ordinal))
            tags.Add("vault");

        if (lower.Contains("settings") || fn.Contains("settings", StringComparison.Ordinal))
            tags.Add("settings");

        if (lower.Contains("mainai") || fn.Contains("mainai", StringComparison.Ordinal) || fn.Contains("ollama", StringComparison.Ordinal))
            tags.Add("ai_main");

        return tags;
    }
}
