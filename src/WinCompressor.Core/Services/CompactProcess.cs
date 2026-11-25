using System.Diagnostics;

namespace WinCompressor.Core.Services;

/// <summary>
/// Shared compact.exe process plumbing used by both the compression and the
/// algorithm-format detection services.
/// </summary>
internal static class CompactProcess
{
    public static string ExecutablePath { get; } = GetCompactExecutablePath();

    private static string GetCompactExecutablePath()
    {
        var systemPath = Path.Combine(Environment.SystemDirectory, "compact.exe");
        return File.Exists(systemPath) ? systemPath : "compact.exe";
    }

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already terminated
            }

            // K9 (inceleme bulgusu): ReadToEndAsync(cancellationToken) task'leri
            // iptalde OCE ile fault eder; gözlemlenmeden bırakılmaları unobserved
            // task exception üretir. Burada sessizce gözlemlenir.
            try
            {
                await Task.WhenAll(stdOutTask, stdErrTask);
            }
            catch
            {
                // İptal akışları: exception bilinçli olarak gözlemlendi.
            }

            throw;
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return (process.ExitCode, stdOut, stdErr);
    }
}
