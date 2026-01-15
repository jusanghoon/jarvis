using System;
using System.Windows;
using System.Windows.Media;

namespace javis.Services.Device;

public static class DeviceDiagnosticsCollector
{
    public static DeviceDiagnosticsSnapshot CaptureBestEffort(Visual? dpiVisual = null)
    {
        var snap = new DeviceDiagnosticsSnapshot();
        try { snap.Fingerprint = DeviceFingerprintProvider.GetFingerprintBestEffort(); } catch { }

        try
        {
            // DIP-based screen size reported by WPF
            var dipW = SystemParameters.PrimaryScreenWidth;
            var dipH = SystemParameters.PrimaryScreenHeight;

            var dpi = dpiVisual != null ? VisualTreeHelper.GetDpi(dpiVisual) : default;
            var scaleX = dpiVisual != null ? dpi.DpiScaleX : 1.0;
            var scaleY = dpiVisual != null ? dpi.DpiScaleY : 1.0;

            var pxW = (int)Math.Round(dipW * scaleX);
            var pxH = (int)Math.Round(dipH * scaleY);

            snap.Display = new DisplayDiagnostics
            {
                PrimaryScreenDipWidth = dipW,
                PrimaryScreenDipHeight = dipH,
                DpiScaleX = scaleX,
                DpiScaleY = scaleY,
                PrimaryScreenPixelWidth = pxW,
                PrimaryScreenPixelHeight = pxH
            };
        }
        catch
        {
            // ignore
        }

        return snap;
    }

    public static string ToHumanSummary(DeviceDiagnosticsSnapshot snap)
    {
        if (snap is null) return "(null)";

        var fp = snap.Fingerprint;
        var d = snap.Display;

        return $"[장치 진단]\n" +
               $"- deviceId: {fp.DeviceId}\n" +
               $"- machine: {fp.MachineName}\n" +
               $"- os: {fp.OsDescription}\n" +
               $"- arch: {fp.ProcessArch}\n" +
               $"- framework: {fp.Framework}\n" +
               $"- display(px): {d.PrimaryScreenPixelWidth}x{d.PrimaryScreenPixelHeight}\n" +
               $"- display(DIP): {d.PrimaryScreenDipWidth:0}x{d.PrimaryScreenDipHeight:0}\n" +
               $"- dpiScale: {d.DpiScaleX:0.##}x{d.DpiScaleY:0.##}\n";
    }
}
