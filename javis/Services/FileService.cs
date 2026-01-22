using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace javis.Services;

public sealed class FileService
{
    public sealed record FileAnalysis(
        string RelativePath,
        long SizeBytes,
        int Lines,
        string[] Namespaces,
        string[] Classes,
        DateTimeOffset LastWriteTime);

    public sealed class AnalysisReport
    {
        public string RootPath { get; init; } = "";
        public IReadOnlyList<FileAnalysis> Files { get; init; } = Array.Empty<FileAnalysis>();

        public int TotalFiles => Files.Count;
        public int TotalLines => Files.Sum(f => f.Lines);

        public IReadOnlyList<FileAnalysis> LargeFiles1000Lines { get; init; } = Array.Empty<FileAnalysis>();
        public IReadOnlyList<FileAnalysis> HighRefCandidateFiles { get; init; } = Array.Empty<FileAnalysis>();

        public override string ToString()
            => $"Files={TotalFiles}, LOC={TotalLines}, Large(>=1000)={LargeFiles1000Lines.Count}, HighRefCandidates={HighRefCandidateFiles.Count}";
    }

    public async Task ApplyRefactorChangeAsync(string filePath, string newContent, Action<string>? progressCallback = null, CancellationToken ct = default)
    {
        filePath = (filePath ?? "").Trim();
        if (filePath.Length == 0) throw new ArgumentException("filePath is empty", nameof(filePath));

        try
        {
            ct.ThrowIfCancellationRequested();

            var full = Path.GetFullPath(filePath);
            if (!File.Exists(full))
                throw new FileNotFoundException(full);

            var dir = Path.GetDirectoryName(full) ?? "";
            var name = Path.GetFileName(full);
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var backup = Path.Combine(dir, $"{name}.bak_{stamp}");

            File.Copy(full, backup, overwrite: false);
            progressCallback?.Invoke($"[BACKUP] 원본 파일의 백업을 생성했습니다: {Path.GetFileName(backup)}");

            ct.ThrowIfCancellationRequested();

            await File.WriteAllTextAsync(full, newContent ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            progressCallback?.Invoke("[WRITE] 리팩토링된 코드가 적용되었습니다.");
        }
        catch (OperationCanceledException)
        {
            progressCallback?.Invoke("[WRITE] 취소됨");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            progressCallback?.Invoke($"[ERROR] 권한이 없어 파일을 수정할 수 없습니다: {ex.Message}");
            throw;
        }
        catch (IOException ex)
        {
            progressCallback?.Invoke($"[ERROR] 파일이 사용 중이거나 I/O 오류가 발생했습니다: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            progressCallback?.Invoke($"[ERROR] ApplyRefactorChangeAsync 실패: {ex.Message}");
            throw;
        }
    }

    public Task RestoreFromBackupAsync(string filePath, Action<string>? progressCallback = null, CancellationToken ct = default)
    {
        filePath = (filePath ?? "").Trim();
        if (filePath.Length == 0) throw new ArgumentException("filePath is empty", nameof(filePath));

        return Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var full = Path.GetFullPath(filePath);
                var dir = Path.GetDirectoryName(full) ?? "";
                var name = Path.GetFileName(full);
                var prefix = name + ".bak_";

                if (!Directory.Exists(dir))
                    throw new DirectoryNotFoundException(dir);

                var latest = Directory.EnumerateFiles(dir, prefix + "*", SearchOption.TopDirectoryOnly)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest == null)
                    throw new FileNotFoundException("No backup files found", prefix + "*");

                File.Copy(latest.FullName, full, overwrite: true);
                progressCallback?.Invoke("[RESTORE] 시스템을 최신 백업 상태로 복구했습니다.");
            }
            catch (OperationCanceledException)
            {
                progressCallback?.Invoke("[RESTORE] 취소됨");
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                progressCallback?.Invoke($"[ERROR] 권한이 없어 복구할 수 없습니다: {ex.Message}");
                throw;
            }
            catch (IOException ex)
            {
                progressCallback?.Invoke($"[ERROR] 파일이 사용 중이거나 I/O 오류가 발생했습니다: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"[ERROR] RestoreFromBackupAsync 실패: {ex.Message}");
                throw;
            }
        }, ct);
    }

    public Task<string> GetFileContentForAnalysisAsync(string filePath, CancellationToken ct = default)
    {
        filePath = (filePath ?? "").Trim();
        if (filePath.Length == 0) throw new ArgumentException("filePath is empty", nameof(filePath));

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var full = Path.GetFullPath(filePath);
            var ext = Path.GetExtension(full);
            if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only .cs/.xaml are allowed");

            if (!File.Exists(full))
                throw new FileNotFoundException(full);

            var fi = new FileInfo(full);
            // safety cap: avoid reading huge files into memory
            const long maxBytes = 2_000_000; // ~2MB
            if (fi.Length > maxBytes)
                throw new InvalidOperationException($"File too large for analysis: {fi.Length} bytes");

            var text = File.ReadAllText(full);
            text = text.Replace("\r\n", "\n");
            return text;
        }, ct);
    }

    private static readonly Regex NamespaceRegex = new(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)", RegexOptions.Compiled);
    private static readonly Regex ClassRegex = new(@"\b(class|record|struct|interface)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    public Task<AnalysisReport> AnalyzeProjectStructureAsync(
        string rootPath,
        Action<string>? progressCallback = null,
        CancellationToken ct = default)
    {
        rootPath = (rootPath ?? "").Trim();
        if (rootPath.Length == 0) throw new ArgumentException("rootPath is empty", nameof(rootPath));

        return Task.Run(() =>
        {
            var rootFull = Path.GetFullPath(rootPath);
            if (!Directory.Exists(rootFull))
                throw new DirectoryNotFoundException(rootFull);

            var allFiles = Directory.EnumerateFiles(rootFull, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var results = new List<FileAnalysis>(allFiles.Length);
            for (var i = 0; i < allFiles.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var full = allFiles[i];
                var rel = Path.GetRelativePath(rootFull, full).Replace('\\', '/');

                progressCallback?.Invoke($"[SYSTEM SCAN] ({i + 1}/{allFiles.Length}) {rel}");

                var fi = new FileInfo(full);
                string text;
                try
                {
                    text = File.ReadAllText(full);
                }
                catch
                {
                    continue;
                }

                var lines = CountLines(text);
                var namespaces = NamespaceRegex.Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
                var classes = ClassRegex.Matches(text).Select(m => m.Groups[2].Value).Distinct(StringComparer.Ordinal).ToArray();

                results.Add(new FileAnalysis(
                    RelativePath: rel,
                    SizeBytes: fi.Length,
                    Lines: lines,
                    Namespaces: namespaces,
                    Classes: classes,
                    LastWriteTime: fi.LastWriteTimeUtc));
            }

            var large = results.Where(r => r.Lines >= 1000).OrderByDescending(r => r.Lines).ToArray();

            // Simple heuristic: candidates that define many types (often correlates with high coupling / refs)
            var highRefCandidates = results
                .Where(r => r.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Where(r => r.Classes.Length >= 6 || r.Lines >= 1200)
                .OrderByDescending(r => r.Classes.Length)
                .ThenByDescending(r => r.Lines)
                .ToArray();

            progressCallback?.Invoke($"[SYSTEM SCAN] 완료. {results.Count}개 파일 분석." );

            return new AnalysisReport
            {
                RootPath = rootFull,
                Files = results,
                LargeFiles1000Lines = large,
                HighRefCandidateFiles = highRefCandidates,
            };
        }, ct);
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 1;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') count++;
        return count;
    }
}
