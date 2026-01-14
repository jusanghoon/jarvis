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
        // Use per-user writable storage so it works even when app is run from Desktop/Program Files.
        // Keep the same base directory as RuntimeSettingsStore.
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis",
            "mainai_file_relevance.json");
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
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    _tagFileScores = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                if (TryLoadFrom(_filePath, out var data))
                {
                    _tagFileScores = data;
                    return;
                }

                // recovery: try .bak
                var bak = _filePath + ".bak";
                if (File.Exists(bak) && TryLoadFrom(bak, out var data2))
                {
                    _tagFileScores = data2;
                    try { File.Copy(bak, _filePath, overwrite: true); } catch { }
                    return;
                }

                // last resort: preserve broken file
                try
                {
                    var corrupt = _filePath + ".corrupt." + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
                    File.Move(_filePath, corrupt, overwrite: true);
                }
                catch { }

                _tagFileScores = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _tagFileScores = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static bool TryLoadFrom(string path, out Dictionary<string, Dictionary<string, int>> data)
    {
        data = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed is null) return false;
            data = new Dictionary<string, Dictionary<string, int>>(parsed, StringComparer.OrdinalIgnoreCase);
            return true;
        }
        catch
        {
            return false;
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

            // Stronger atomic save: write temp then replace.
            // If Replace isn't available/supported, fall back to copy.
            var tmp = _filePath + ".tmp";
            var bak = _filePath + ".bak";

            File.WriteAllText(tmp, json);

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Replace(tmp, _filePath, bak, ignoreMetadataErrors: true);
                    try { File.Delete(bak); } catch { }
                }
                else
                {
                    File.Move(tmp, _filePath);
                }
            }
            catch
            {
                // fallback
                File.Copy(tmp, _filePath, overwrite: true);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
        catch
        {
            // ignore
        }
    }
}
