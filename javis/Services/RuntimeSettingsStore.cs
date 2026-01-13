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
        public string? MainAiName { get; set; }
        public bool? HomeRightPanelEnabled { get; set; }
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

            if (!string.IsNullOrWhiteSpace(dto.MainAiName))
                settings.MainAiName = dto.MainAiName;

            if (dto.HomeRightPanelEnabled is bool b)
                settings.HomeRightPanelEnabled = b;
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
                MainAiName = settings.MainAiName,
                HomeRightPanelEnabled = settings.HomeRightPanelEnabled
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
