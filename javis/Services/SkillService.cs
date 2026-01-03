using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Timers;
using javis.Models;

namespace javis.Services;

public sealed class SkillService : IDisposable
{
    public static SkillService Instance { get; } = new();

    public event Action? SkillsChanged;

    public string SkillsRoot { get; }

    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounce;

    private SkillService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis");
        Directory.CreateDirectory(dataDir);

        SkillsRoot = Path.Combine(dataDir, "skills");
        Directory.CreateDirectory(SkillsRoot);

        EnsureSampleSkill();

        _debounce = new System.Timers.Timer(250) { AutoReset = false };
        _debounce.Elapsed += (_, __) => SkillsChanged?.Invoke();

        _watcher = new FileSystemWatcher(SkillsRoot)
        {
            IncludeSubdirectories = true,
            Filter = "*.json",
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
        };

        _watcher.Changed += (_, __) => DebouncedRaise();
        _watcher.Created += (_, __) => DebouncedRaise();
        _watcher.Deleted += (_, __) => DebouncedRaise();
        _watcher.Renamed += (_, __) => DebouncedRaise();
    }

    private void DebouncedRaise()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public List<Skill> LoadSkills()
    {
        var result = new List<Skill>();

        // 1) legacy folder-based skills: {dir}/manifest.json
        foreach (var dir in Directory.GetDirectories(SkillsRoot))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            TryLoadSkillManifest(manifestPath, dir, result);
        }

        // 2) new file-based skills: {SkillsRoot}/*.skill.json
        foreach (var path in Directory.EnumerateFiles(SkillsRoot, "*.skill.json", SearchOption.TopDirectoryOnly))
        {
            var folder = Path.GetDirectoryName(path) ?? SkillsRoot;
            TryLoadSkillManifest(path, folder, result);
        }

        return result
            .OrderBy(s => s.Manifest.Name)
            .ToList();
    }

    private static void TryLoadSkillManifest(string manifestPath, string folder, List<Skill> result)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<SkillManifest>(json) ?? new SkillManifest();

            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                if (Path.GetFileName(manifestPath).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                    manifest.Id = Path.GetFileName(folder);
                else
                    manifest.Id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(manifestPath));
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
                manifest.Name = manifest.Id;

            result.Add(new Skill { Manifest = manifest, FolderPath = folder });
        }
        catch
        {
            // ignore broken manifest
        }
    }

    private void EnsureSampleSkill()
    {
        var folder = Path.Combine(SkillsRoot, "hello");
        var manifestPath = Path.Combine(folder, "manifest.json");

        if (File.Exists(manifestPath)) return;

        Directory.CreateDirectory(folder);
        var sample = new SkillManifest
        {
            Id = "hello",
            Name = "인사하기",
            Description = "자비스에게 인사 프롬프트를 보냅니다",
            Type = "prompt",
            Prompt = "안녕 자비스. 오늘 일정과 할 일을 3줄로 요약해줘."
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
