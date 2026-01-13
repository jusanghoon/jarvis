using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace javis.Services.MainAi;

public sealed class MainAiFileRelevanceStore
{
    private readonly string _filePath;
    private readonly object _gate = new();

    private Dictionary<string, Dictionary<string, int>> _tagFileScores = new(StringComparer.OrdinalIgnoreCase);

    public MainAiFileRelevanceStore(string solutionRoot)
    {
        if (string.IsNullOrWhiteSpace(solutionRoot))
            solutionRoot = AppDomain.CurrentDomain.BaseDirectory;

        var root = Path.GetFullPath(solutionRoot);
        _filePath = Path.Combine(root, "javis", "AppData", "mainai_file_relevance.json");
        Load();
    }

    public void RecordFeedback(IEnumerable<string> tags, IEnumerable<string> relPaths, bool helpful)
    {
        if (tags is null || relPaths is null) return;

        var delta = helpful ? 2 : -1;

        lock (_gate)
        {
            foreach (var t in tags.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_tagFileScores.TryGetValue(t, out var map))
                {
                    map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    _tagFileScores[t] = map;
                }

                foreach (var p in relPaths.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    map.TryGetValue(p, out var cur);
                    var next = cur + delta;
                    if (next < -50) next = -50;
                    if (next > 500) next = 500;
                    map[p] = next;
                }
            }

            Save();
        }
    }

    public IReadOnlyDictionary<string, int> GetBoostsForTag(string tag)
    {
        tag = (tag ?? "").Trim();
        if (tag.Length == 0) return new Dictionary<string, int>();

        lock (_gate)
        {
            if (_tagFileScores.TryGetValue(tag, out var map))
                return new Dictionary<string, int>(map, StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, int>();
    }

    private void Load()
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_filePath))
                {
                    _tagFileScores = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _tagFileScores = data ?? new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _tagFileScores = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_tagFileScores, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // ignore
        }
    }
}
