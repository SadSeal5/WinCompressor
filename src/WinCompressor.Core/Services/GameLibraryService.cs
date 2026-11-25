using System.IO;
using Microsoft.Win32;
using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

/// <summary>
/// Yaygın oyun kütüphanelerinin kurulum klasörlerini tespit eder (Faz 5.5):
/// Steam (registry), Epic / EA / GOG / Xbox (bilinen varsayılan yollar),
/// Ubisoft (registry). Her aday yolu Directory.Exists ile doğrular.
/// </summary>
public class GameLibraryService : IGameLibraryService
{
    private readonly IRegistryReader _registry;
    private readonly Func<string, bool> _directoryExists;

    public GameLibraryService(IRegistryReader? registry = null, Func<string, bool>? directoryExists = null)
    {
        _registry = registry ?? new LocalMachineRegistryReader();
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public IReadOnlyList<GameLibraryInfo> DetectInstalledLibraries()
    {
        var found = new List<GameLibraryInfo>();

        // Steam: HKLM\SOFTWARE\WOW6432Node\Valve\Steam → InstallPath, oyunlar steamapps\common altında.
        var steamRoot = _registry.LocalMachine(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (TryResolve(steamRoot, subFolder: @"steamapps\common", out var steamCommon))
        {
            found.Add(new GameLibraryInfo("Steam", steamCommon, "🎮"));
        }

        // Epic Games Launcher: kurulumda Steam gibi güvenilir bir registry anahtarı yok;
        // klasik varsayılan yol + ikincil konum denenir.
        foreach (var epicPath in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games")
                 })
        {
            if (_directoryExists(epicPath))
            {
                found.Add(new GameLibraryInfo("Epic Games", epicPath, "🎮"));
                break;
            }
        }

        // Ubisoft Connect: HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher → InstallDir.
        var ubisoftDir = _registry.LocalMachine(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir");
        if (TryResolve(ubisoftDir, null, out var ubisoft))
        {
            found.Add(new GameLibraryInfo("Ubisoft Connect", ubisoft, "🎮"));
        }

        // EA app: modern Kurulum varsayılanları.
        foreach (var eaPath in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games"),
                     @"C:\Program Files\EA App"
                 })
        {
            if (_directoryExists(eaPath))
            {
                found.Add(new GameLibraryInfo("EA App", eaPath, "🎮"));
                break;
            }
        }

        // GOG Galaxy: oyunlar C:\GOG Games'e kurulur.
        if (_directoryExists(@"C:\GOG Games"))
        {
            found.Add(new GameLibraryInfo("GOG Galaxy", @"C:\GOG Games", "🎮"));
        }

        // Xbox / PC Game Pass: modern varsayılan.
        if (_directoryExists(@"C:\XboxGames"))
        {
            found.Add(new GameLibraryInfo("Xbox Game Pass", @"C:\XboxGames", "🎮"));
        }

        return found;
    }

    /// <summary>Registry değerini yola çevirir, (opsiyonel alt klasörle) doğrular.</summary>
    private bool TryResolve(string? root, string? subFolder, out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(root))
            return false;

        var candidate = string.IsNullOrEmpty(subFolder)
            ? root.Trim()
            : Path.Combine(root.Trim().TrimEnd('\\'), subFolder);

        if (!_directoryExists(candidate))
            return false;

        resolved = candidate;
        return true;
    }

    private sealed class LocalMachineRegistryReader : IRegistryReader
    {
        // Registry API'si Windows-only; Core kütüphanesi platformdan bağımsız
        // net10.0 hedefli. Bu sınıf yalnızca Windows'ta (üretim GUI) inşa edilir
        // ve DI sayesinde testler sahte okuyucu kullanır — burada gürültüyü bastır.
#pragma warning disable CA1416
        public string? LocalMachine(string subKey, string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKey);
                return key?.GetValue(valueName) as string;
            }
            catch
            {
                return null;
            }
        }
#pragma warning restore CA1416
    }
}
