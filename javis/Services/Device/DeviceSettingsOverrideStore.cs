using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace javis.Services.Device;

public sealed class DeviceSettingsOverrideStore
{
    private readonly string _dir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DeviceSettingsOverrideStore(string dataDir)
    {
        _dir = Path.Combine(dataDir, "device");
        Directory.CreateDirectory(_dir);
    }

    private string GetPath(string deviceId)
        => Path.Combine(_dir, $"settings_override.{Sanitize(deviceId)}.json");

    public DeviceSettingsOverride Load(string deviceId)
    {
        var ov = new DeviceSettingsOverride { DeviceId = deviceId };
        try
        {
            var path = GetPath(deviceId);
            if (!File.Exists(path)) return ov;
            var json = File.ReadAllText(path, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<DeviceSettingsOverride>(json, JsonOpts);
            if (loaded is null) return ov;
            loaded.DeviceId = string.IsNullOrWhiteSpace(loaded.DeviceId) ? deviceId : loaded.DeviceId;
            return loaded;
        }
        catch
        {
            return ov;
        }
    }

    public void Save(DeviceSettingsOverride ov)
    {
        if (ov is null) return;
        var deviceId = string.IsNullOrWhiteSpace(ov.DeviceId) ? "unknown" : ov.DeviceId;
        ov.DeviceId = deviceId;
        ov.Ts = DateTimeOffset.Now.ToString("O");

        try
        {
            var path = GetPath(deviceId);
            var json = JsonSerializer.Serialize(ov, JsonOpts);
            File.WriteAllText(path, json, new UTF8Encoding(true));
        }
        catch
        {
            // ignore
        }
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "unknown";
        foreach (var ch in Path.GetInvalidFileNameChars())
            s = s.Replace(ch, '_');
        return s;
    }
}
