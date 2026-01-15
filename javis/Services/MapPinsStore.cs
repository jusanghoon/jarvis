using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using javis.ViewModels;

namespace javis.Services;

internal sealed class MapPinsStore
{
    private readonly string _pinsPath;

    public MapPinsStore()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
        var mapsDir = Path.Combine(dataDir, "maps");
        Directory.CreateDirectory(mapsDir);
        _pinsPath = Path.Combine(mapsDir, "pins.json");
    }

    public IReadOnlyList<MapPin> Load()
    {
        try
        {
            if (!File.Exists(_pinsPath))
                return Array.Empty<MapPin>();

            var json = File.ReadAllText(_pinsPath);
            return JsonSerializer.Deserialize<MapPin[]>(json) ?? Array.Empty<MapPin>();
        }
        catch
        {
            return Array.Empty<MapPin>();
        }
    }

    public void Save(IEnumerable<MapPin> pins)
    {
        try
        {
            var json = JsonSerializer.Serialize(pins.ToArray(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_pinsPath, json);
        }
        catch { }
    }
}
