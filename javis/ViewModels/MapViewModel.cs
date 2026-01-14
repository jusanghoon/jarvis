using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace javis.ViewModels;

public partial class MapViewModel : ObservableObject
{
    private readonly string _pinsPath;

    public ObservableCollection<MapPin> Pins { get; } = new();

    [ObservableProperty]
    private string locationInput = "";

    public IRelayCommand AddPinCommand { get; }

    public MapViewModel()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
        var mapsDir = Path.Combine(dataDir, "maps");
        Directory.CreateDirectory(mapsDir);
        _pinsPath = Path.Combine(mapsDir, "pins.json");

        AddPinCommand = new RelayCommand(AddPin);

        LoadPins();
    }

    private void AddPin()
    {
        var name = (LocationInput ?? "").Trim();
        if (name.Length == 0) return;

        if (Pins.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            LocationInput = "";
            return;
        }

        // Placeholder coordinate: spread pins across the canvas deterministically
        var idx = Pins.Count;
        var x = 20 + (idx * 22) % 520;
        var y = 40 + ((idx * 31) % 300);

        Pins.Add(new MapPin { Name = name, X = x, Y = y });
        LocationInput = "";
        SavePins();
    }

    private void LoadPins()
    {
        try
        {
            if (!File.Exists(_pinsPath))
                return;

            var json = File.ReadAllText(_pinsPath);
            var pins = JsonSerializer.Deserialize<MapPin[]>(json);
            if (pins is null) return;

            Pins.Clear();
            foreach (var p in pins)
                Pins.Add(p);
        }
        catch
        {
            // best-effort
        }
    }

    private void SavePins()
    {
        try
        {
            var json = JsonSerializer.Serialize(Pins.ToArray(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_pinsPath, json);
        }
        catch
        {
            // best-effort
        }
    }
}

public sealed class MapPin
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}
