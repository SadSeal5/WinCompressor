using System.IO;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;

namespace WinCompressor.UI.ViewModels;

/// <summary>
/// Ayar kalıcılığı kısmı: son oturumun tercihlerini uygular ve mevcut
/// durumu yakalar (pencere konumu/boyutu pencere katmanı tarafından eklenir).
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Ayar dosyasından yüklenen dil tercihi ("tr"/"en"). İnceleme bulgusu K3:
    /// <see cref="CaptureSettings"/> bu değeri geri yazmazsa pencere kapanışında
    /// kaydedilen ayarlar diskteki dil tercihini "tr" ile eziyordu.
    /// </summary>
    private string _persistedLanguage = "tr";

    /// <summary>
    /// Restores persisted preferences right after construction. Invalid or
    /// privileged values fall back safely:
    /// nonexistent paths, undefined enum names and admin-only algorithms are
    /// ignored for standard users.
    /// </summary>
    private void ApplyLoadedSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _persistedLanguage = string.IsNullOrWhiteSpace(settings.Language)
            ? "tr"
            : settings.Language.Trim();

        if (!string.IsNullOrWhiteSpace(settings.LastPath) && Directory.Exists(settings.LastPath))
        {
            _selectedPath = settings.LastPath;
            OnPropertyChanged(nameof(SelectedPath));
        }

        IncludeSubdirectories = settings.IncludeSubdirectories;
        OnlyUncompressed = settings.OnlyUncompressed;
        IncludeAppData = settings.IncludeAppData;
        IsActivityLogVisible = settings.ActivityLogVisible;

        if (Enum.TryParse(settings.LastCategory, out FileCategory category) &&
            Enum.IsDefined(typeof(FileCategory), category))
        {
            _selectedCategory = category;
            OnPropertyChanged(nameof(SelectedCategory));
        }

        if (Enum.TryParse(settings.LastAlgorithm, out CompressionAlgorithm algorithm) &&
            Enum.IsDefined(typeof(CompressionAlgorithm), algorithm))
        {
            // Admin-only algorithms are not restored for standard users; the
            // default (Ntfs) stays in place so no operation is refused at startup.
            if (!algorithm.RequiresAdmin() || _isAdministrator)
            {
                SelectedAlgorithm = algorithm;
            }
        }
    }

    /// <summary>
    /// Snapshots the current scan/UI preferences into a settings object.
    /// The window layer adds its own bounds fields and then persists both.
    /// </summary>
    public AppSettings CaptureSettings()
    {
        return new AppSettings
        {
            LastPath = SelectedPath,
            LastAlgorithm = SelectedAlgorithm.ToString(),
            LastCategory = SelectedCategory.ToString(),
            IncludeSubdirectories = IncludeSubdirectories,
            OnlyUncompressed = OnlyUncompressed,
            IncludeAppData = IncludeAppData,
            ActivityLogVisible = IsActivityLogVisible,
            // K3: dil tercihi VM tarafında kullanılmasa da gidiş-dönüş
            // korunmalı — aksi hâlde kapanışta varsayılanla ezilir.
            Language = _persistedLanguage
        };
    }

    /// <summary>
    /// Persists the current preferences now (no-op without a settings service,
    /// e.g. in tests). Call before app shutdown or at noteworthy moments.
    /// </summary>
    public void PersistSettings()
    {
        _settingsService?.Save(CaptureSettings());
    }
}
