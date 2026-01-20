using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace javis.Services;

public static class RuntimeSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string DataDir
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");

    private static string SettingsPath
        => Path.Combine(DataDir, "runtime_settings.json");

    private sealed class Dto
    {
        public string? Model { get; set; }
        public string? AiModelName { get; set; }
        public string? MainAiName { get; set; }
        public bool? HomeRightPanelEnabled { get; set; }
        public double? UiScale { get; set; }
        public bool? DisableScaleToFit { get; set; }
        public bool? SettingsShowResolution { get; set; }
        public bool? LocalDeviceDiagnosticsEnabled { get; set; }
    }

    public static void LoadInto(RuntimeSettings settings)
    {
        if (settings is null) return;

        try
        {
            if (!File.Exists(SettingsPath)) return;

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var dto = JsonSerializer.Deserialize<Dto>(json, JsonOpts);
            if (dto is null) return;

            if (!string.IsNullOrWhiteSpace(dto.Model))
                settings.Model = dto.Model;

            if (!string.IsNullOrWhiteSpace(dto.AiModelName))
                settings.AiModelName = dto.AiModelName;

            if (!string.IsNullOrWhiteSpace(dto.MainAiName))
                settings.MainAiName = dto.MainAiName;

            if (dto.HomeRightPanelEnabled is bool b)
                settings.HomeRightPanelEnabled = b;

            if (dto.UiScale is double s && !double.IsNaN(s) && !double.IsInfinity(s) && s > 0)
                settings.UiScale = s;

            if (dto.DisableScaleToFit is bool ds)
                settings.DisableScaleToFit = ds;

            if (dto.SettingsShowResolution is bool sr)
                settings.SettingsShowResolution = sr;

            if (dto.LocalDeviceDiagnosticsEnabled is bool dd)
                settings.LocalDeviceDiagnosticsEnabled = dd;
        }
        catch
        {
            // ignore broken settings file
        }
    }

    public static void SaveFrom(RuntimeSettings settings)
    {
        if (settings is null) return;

        try
        {
            Directory.CreateDirectory(DataDir);

            var dto = new Dto
            {
                Model = settings.Model,
                AiModelName = settings.AiModelName,
                MainAiName = settings.MainAiName,
                HomeRightPanelEnabled = settings.HomeRightPanelEnabled,
                UiScale = settings.UiScale,
                DisableScaleToFit = settings.DisableScaleToFit,
                SettingsShowResolution = settings.SettingsShowResolution,
                LocalDeviceDiagnosticsEnabled = settings.LocalDeviceDiagnosticsEnabled
            };

            var json = JsonSerializer.Serialize(dto, JsonOpts);
            File.WriteAllText(SettingsPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch
        {
            // never crash on settings persist
        }
    }
}
