using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jarvis.Core.System;

public interface ISystemActionExecutor
{
    Task<ExecutionResult> ExecuteAppAsync(string exePath, string arguments, CancellationToken cancellationToken = default);

    Task<ExecutionResult> OpenLocationAsync(string pathOrUrl, CancellationToken cancellationToken = default);

    Task<ExecutionResult<string>> RunShellCommandAsync(string command, CancellationToken cancellationToken = default);
}

public sealed class SystemActionExecutor : ISystemActionExecutor
{
    private static readonly TimeSpan DefaultShellTimeout = TimeSpan.FromSeconds(8);

    public Task<ExecutionResult> ExecuteAppAsync(string exePath, string arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(exePath))
            return Task.FromResult(ExecutionResult.Fail("exePath is required."));

        string normalized;
        try
        {
            normalized = Path.GetFullPath(exePath);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ExecutionResult.Fail($"Invalid path: {ex.Message}"));
        }

        if (!File.Exists(normalized))
            return Task.FromResult(ExecutionResult.Fail($"File not found: {normalized}"));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = normalized,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
            };

            _ = Process.Start(psi);
            return Task.FromResult(ExecutionResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ExecutionResult.Fail(ex.Message));
        }
    }

    public Task<ExecutionResult> OpenLocationAsync(string pathOrUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return Task.FromResult(ExecutionResult.Fail("pathOrUrl is required."));

        // URL
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = uri.ToString(),
                    UseShellExecute = true,
                };

                _ = Process.Start(psi);
                return Task.FromResult(ExecutionResult.Ok());
            }
            catch (Exception ex)
            {
                return Task.FromResult(ExecutionResult.Fail(ex.Message));
            }
        }

        // Local path
        string normalized;
        try
        {
            normalized = Path.GetFullPath(pathOrUrl);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ExecutionResult.Fail($"Invalid path: {ex.Message}"));
        }

        if (!Directory.Exists(normalized) && !File.Exists(normalized))
            return Task.FromResult(ExecutionResult.Fail($"Path not found: {normalized}"));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = normalized,
                UseShellExecute = true,
            };

            _ = Process.Start(psi);
            return Task.FromResult(ExecutionResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ExecutionResult.Fail(ex.Message));
        }
    }

    public async Task<ExecutionResult<string>> RunShellCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return ExecutionResult<string>.Fail("command is required.");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{EscapeForPowerShellDoubleQuoted(command)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            if (!proc.Start())
                return ExecutionResult<string>.Fail("Failed to start PowerShell process.");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DefaultShellTimeout);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                return ExecutionResult<string>.Fail("PowerShell timed out. 프로그램이 응답하지 않습니다. 직접 실행하시겠습니까?");
            }

            var stdout = await proc.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            if (proc.ExitCode != 0)
            {
                var msg = string.IsNullOrWhiteSpace(stderr)
                    ? $"PowerShell failed with exit code {proc.ExitCode}."
                    : stderr.Trim();

                return ExecutionResult<string>.Fail(msg);
            }

            return ExecutionResult<string>.Ok(stdout);
        }
        catch (OperationCanceledException)
        {
            return ExecutionResult<string>.Fail("Canceled.");
        }
        catch (Exception ex)
        {
            return ExecutionResult<string>.Fail(ex.Message);
        }
    }

    private static string EscapeForPowerShellDoubleQuoted(string value)
        => value.Replace("`", "``", StringComparison.Ordinal)
                .Replace("\"", "`\"", StringComparison.Ordinal);
}
