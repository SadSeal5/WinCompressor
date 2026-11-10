using System.Diagnostics;
using System.Security.Principal;

namespace WinCompressor.Core.Services;

public class AdminService : IAdminService
{
    public bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public bool RestartAsAdmin(string? arguments = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            Process.Start(psi);
            Environment.Exit(0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
