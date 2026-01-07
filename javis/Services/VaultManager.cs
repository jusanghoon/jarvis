using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace javis.Services;

public sealed class VaultManager
{
    public string VaultDir { get; }
    public string InboxDir { get; }
    public string ManifestPath { get; }

    public VaultManager(string dataDir)
    {
        VaultDir = Path.Combine(dataDir, "vault");
        InboxDir = Path.Combine(VaultDir, "inbox");
        ManifestPath = Path.Combine(VaultDir, "manifest.jsonl");

        Directory.CreateDirectory(VaultDir);
        Directory.CreateDirectory(InboxDir);
    }

    public async Task<IReadOnlyList<VaultItem>> ImportAsync(IEnumerable<string> filePaths)
    {
        var results = new List<VaultItem>();

        foreach (var src in filePaths.Where(File.Exists))
        {
            var fi = new FileInfo(src);
            var sha = await ComputeSha256Async(src);

            var safeName = MakeSafeFileName(fi.Name);
            var destName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{sha[..12]}-{safeName}";
            var destPath = Path.Combine(InboxDir, destName);

            File.Copy(src, destPath, overwrite: false);

            var item = new VaultItem(
                addedAt: DateTimeOffset.Now,
                originalPath: src,
                storedPath: destPath,
                fileName: fi.Name,
                sizeBytes: fi.Length,
                sha256: sha,
                ext: fi.Extension.ToLowerInvariant()
            );

            await AppendManifestAsync(item);
            results.Add(item);
        }

        return results;
    }

    private async Task AppendManifestAsync(VaultItem item)
    {
        var line = JsonSerializer.Serialize(item) + "\n";
        await File.AppendAllTextAsync(ManifestPath, line, Encoding.UTF8);
    }

    private static string MakeSafeFileName(string name)
    {
        var safe = new string(name.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ' ' ? ch : '_'
        ).ToArray());

        return safe.Trim();
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public IReadOnlyList<VaultItem> ReadRecent(int maxItems = 10)
    {
        if (!File.Exists(ManifestPath)) return Array.Empty<VaultItem>();

        var list = new List<VaultItem>();
        foreach (var line in File.ReadLines(ManifestPath).Reverse())
        {
            try
            {
                var it = JsonSerializer.Deserialize<VaultItem>(line, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (it != null) list.Add(it);
            }
            catch { }

            if (list.Count >= maxItems) break;
        }

        list.Reverse();
        return list;
    }
}

public sealed record VaultItem(
    DateTimeOffset addedAt,
    string originalPath,
    string storedPath,
    string fileName,
    long sizeBytes,
    string sha256,
    string ext
);
