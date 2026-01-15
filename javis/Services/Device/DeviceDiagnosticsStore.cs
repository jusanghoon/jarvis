using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace javis.Services.Device;

public sealed class DeviceDiagnosticsStore
{
    private readonly string _dir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DeviceDiagnosticsStore(string dataDir)
    {
        _dir = Path.Combine(dataDir, "device");
        Directory.CreateDirectory(_dir);
    }

    public string GetLatestPath(string deviceId)
        => Path.Combine(_dir, $"diagnostics.{Sanitize(deviceId)}.latest.json");

    public void SaveLatest(DeviceDiagnosticsSnapshot snap)
    {
        if (snap is null) return;
        var deviceId = snap.Fingerprint?.DeviceId ?? "unknown";

        var path = GetLatestPath(deviceId);
        var json = JsonSerializer.Serialize(snap, JsonOpts);
        File.WriteAllText(path, json, new UTF8Encoding(true));
    }

    public DeviceDiagnosticsSnapshot? LoadLatest(string deviceId)
    {
        try
        {
            var path = GetLatestPath(deviceId);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<DeviceDiagnosticsSnapshot>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<string> ListDeviceIds()
    {
        try
        {
            return Directory.EnumerateFiles(_dir, "diagnostics.*.latest.json")
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Select(n => n!)
                .Select(n => n.Substring("diagnostics.".Length))
                .Select(n => n.Substring(0, n.Length - ".latest.json".Length))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString();
    }
}
