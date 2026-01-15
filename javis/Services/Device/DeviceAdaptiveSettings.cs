using System;

namespace javis.Services.Device;

public static class DeviceAdaptiveSettings
{
    public static void ApplyAdaptiveDefaultsBestEffort(RuntimeSettings settings, DeviceDiagnosticsSnapshot snap)
    {
        if (settings is null || snap is null) return;

        // Only apply if user has not changed from default-like value.
        // Current default is 0.9; treat 0 or invalid as unset.
        var current = settings.UiScale;
        if (double.IsNaN(current) || double.IsInfinity(current) || current <= 0)
            current = 0.9;

        var dipW = snap.Display.PrimaryScreenDipWidth;
        var dipH = snap.Display.PrimaryScreenDipHeight;
        var dpiScaleX = snap.Display.DpiScaleX;

        // Heuristic: smaller laptop screens or high DPI scaling often need a bit smaller UI.
        // If primary display width in pixels is low-ish or DPI scale is high, downscale slightly.
        var suggested = current;

        try
        {
            var pxW = snap.Display.PrimaryScreenPixelWidth;
            if (pxW > 0 && pxW <= 1920)
                suggested = Math.Min(suggested, 0.85);
        }
        catch { }

        try
        {
            if (dpiScaleX >= 1.5)
                suggested = Math.Min(suggested, 0.8);
            else if (dpiScaleX >= 1.25)
                suggested = Math.Min(suggested, 0.85);
        }
        catch { }

        try
        {
            if (dipW > 0 && dipW <= 1280)
                suggested = Math.Min(suggested, 0.8);
        }
        catch { }

        if (suggested <= 0) suggested = 0.9;

        // Apply only if it results in a change; avoid churn.
        if (Math.Abs(settings.UiScale - suggested) > 0.0001)
            settings.UiScale = suggested;
    }
}
