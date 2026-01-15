using System;

namespace javis.Services.Device;

public sealed class DeviceFingerprint
{
    public string DeviceId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string OsDescription { get; set; } = "";
    public string ProcessArch { get; set; } = "";
    public string Framework { get; set; } = "";
}

public sealed class DisplayDiagnostics
{
    public int PrimaryScreenPixelWidth { get; set; }
    public int PrimaryScreenPixelHeight { get; set; }
    public double PrimaryScreenDipWidth { get; set; }
    public double PrimaryScreenDipHeight { get; set; }
    public double DpiScaleX { get; set; }
    public double DpiScaleY { get; set; }
}

public sealed class DeviceDiagnosticsSnapshot
{
    public string Ts { get; set; } = DateTimeOffset.Now.ToString("O");
    public DeviceFingerprint Fingerprint { get; set; } = new();
    public DisplayDiagnostics Display { get; set; } = new();
}
