using System;

namespace javis.Services.Device;

public sealed class DeviceSettingsOverride
{
    public string DeviceId { get; set; } = "";
    public string Ts { get; set; } = DateTimeOffset.Now.ToString("O");

    public double? UiScaleOverride { get; set; }

    public static bool IsValidUiScale(double v)
        => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0.1 && v <= 3.0;
}
