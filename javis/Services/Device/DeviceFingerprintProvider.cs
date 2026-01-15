using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace javis.Services.Device;

public static class DeviceFingerprintProvider
{
    public static DeviceFingerprint GetFingerprintBestEffort()
    {
        var fp = new DeviceFingerprint();

        try { fp.MachineName = Environment.MachineName; } catch { }
        try { fp.UserName = Environment.UserName; } catch { }
        try { fp.OsDescription = RuntimeInformation.OSDescription; } catch { }
        try { fp.ProcessArch = RuntimeInformation.ProcessArchitecture.ToString(); } catch { }

        try
        {
            var asm = Assembly.GetEntryAssembly();
            fp.Framework = asm?.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName ?? "";
        }
        catch { }

        fp.DeviceId = ComputeStableDeviceId(fp);
        return fp;
    }

    private static string ComputeStableDeviceId(DeviceFingerprint fp)
    {
        try
        {
            // Local-only: combine a few coarse local identifiers and hash.
            var raw = string.Join("|", new[]
            {
                fp.MachineName ?? "",
                fp.OsDescription ?? "",
                fp.ProcessArch ?? "",
                fp.Framework ?? ""
            });

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return "dev_" + Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 16);
        }
        catch
        {
            // Fallback to machine name hash only.
            try
            {
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(fp.MachineName ?? "unknown"));
                return "dev_" + Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 16);
            }
            catch
            {
                return "dev_unknown";
            }
        }
    }
}
