using System.Diagnostics;
using System.IO;
using System.Windows;
using WinCompressor.UI.Converters;

namespace WinCompressor.UI.ViewModels;

/// <summary>
/// Dosya başına UI komutları: konum açma, dosyayı açma, yol kopyalama,
/// listeden çıkarma, seçim yardımcıları.
/// </summary>
public partial class MainViewModel
{
    private void ExecuteOpenFileLocation(FileItemViewModel? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
            return;

        if (OpenFileLocationHandler != null)
        {
            OpenFileLocationHandler(item.FilePath);
            LogActivity($"Dosya konumu açıldı: {item.FileName}");
            return;
        }

        try
        {
            if (File.Exists(item.FilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.FilePath}\"",
                    UseShellExecute = true
                });
                LogActivity($"Dosya konumu açıldı: {item.FileName}");
            }
            else
            {
                // T3-N3 (inceleme turu 3): eksik dosyada sessiz kalmak yerine
                // ExecuteOpenFile'daki "Dosya bulunamadı" desenine uyulur.
                LogWarning($"Dosya bulunamadı: {item.FilePath}");
                StatusMessage = $"{item.FileName} artık diskte yok.";
            }
        }
        catch (Exception ex)
        {
            LogActivity($"Dosya konumu açılamadı: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the file itself with the shell's default association
    /// (double-click / Enter in the grid).
    /// </summary>
    public void ExecuteOpenFile(FileItemViewModel? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
            return;

        if (!File.Exists(item.FilePath))
        {
            StatusMessage = $"Dosya bulunamadı: {item.FileName}";
            LogWarning($"Dosya açılamadı, dosya bulunamadı: {item.FileName}");
            return;
        }

        if (OpenFileHandler != null)
        {
            OpenFileHandler(item.FilePath);
            LogActivity($"Dosya açıldı: {item.FileName}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.FilePath,
                UseShellExecute = true
            });
            LogActivity($"Dosya açıldı: {item.FileName}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dosya açılamadı: {ex.Message}";
            LogError($"Dosya açılamadı ({item.FileName}): {ex.Message}");
        }
    }

    private void ExecuteCopyPath(FileItemViewModel? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
            return;

        if (ClipboardHandler != null)
        {
            ClipboardHandler(item.FilePath);
        }
        else
        {
            try
            {
                Clipboard.SetText(item.FilePath);
            }
            catch (Exception ex)
            {
                LogActivity($"Panoya kopyalanamadı: {ex.Message}");
                return;
            }
        }

        LogActivity($"Dosya yolu kopyalandı: {item.FilePath}");
    }

    public void ExecuteExcludeFromList(FileItemViewModel? item)
    {
        if (item == null)
            return;

        if (Files.Remove(item))
        {
            _scannedPaths.Remove(item.FilePath);
            TotalFilesFound = Files.Count;
            TotalBytesFound = Files.Sum(f => f.Size);
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            ApplyFilter();
            LogActivity($"Dosya listeden çıkarıldı: {item.FileName}");
            StatusMessage = $"{item.FileName} listeden çıkarıldı.";
        }
    }

    public void ExecuteInvertSelection()
    {
        if (IsBusy || Files.Count == 0)
            return;

        _isUpdatingSelectionAll = true;
        try
        {
            foreach (var file in Files)
            {
                file.IsSelected = !file.IsSelected;
            }
        }
        finally
        {
            _isUpdatingSelectionAll = false;
        }

        UpdateSelectionMetrics();
        LogActivity("Dosya seçimi tersine çevrildi.");
    }

    public void ExecuteSelectLargeFiles()
    {
        if (IsBusy || Files.Count == 0)
            return;

        _isUpdatingSelectionAll = true;
        try
        {
            foreach (var file in Files)
            {
                file.IsSelected = file.Size >= LargeFileThreshold;
            }
        }
        finally
        {
            _isUpdatingSelectionAll = false;
        }

        UpdateSelectionMetrics();
        LogActivity($"Büyük dosyalar (>= 50 MB) seçildi: {SelectedFilesCount} dosya.");
    }
}
