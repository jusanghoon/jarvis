using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using javis.Services;

namespace javis.ViewModels;

public partial class MapViewModel : ObservableObject
{
    private readonly MapPinsStore _store;

    public ObservableCollection<MapPin> Pins { get; } = new();

    [ObservableProperty]
    private string locationInput = "";

    public IRelayCommand AddPinCommand { get; }

    public MapViewModel()
    {
        _store = new MapPinsStore();

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
            Pins.Clear();
            foreach (var p in _store.Load())
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
            _store.Save(Pins);
        }
        catch
        {
            // best-effort
        }
    }

    public void Reload() => LoadPins();
}

public sealed class MapPin
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}
