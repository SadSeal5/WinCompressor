using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface IGameLibraryService
{
    /// <summary>
    /// Bu makinede kurulu olduğunu doğruladığı oyun kütüphanesi klasörlerini
    /// döndürür; yoksa boş liste. Yollar registry + bilinen varsayılanlar
    /// üzerinden çözümlenir ve Directory.Exists ile doğrulanır.
    /// </summary>
    IReadOnlyList<GameLibraryInfo> DetectInstalledLibraries();
}

/// <summary>
/// HKLM registry okuma soyutlaması (birim testleri sahte okuyucuyla çalışır;
/// üretim gerçek registry kullanır — okuma için yönetici yetkisi gerekmez).
/// </summary>
public interface IRegistryReader
{
    string? LocalMachine(string subKey, string valueName);
}
