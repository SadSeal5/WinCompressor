using System.IO;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.Converters;

namespace WinCompressor.UI.ViewModels;

/// <summary>
/// Faz 5 ileri özellikler: geri alma güvenlik ağı (5.4), kurulu oyun
/// kütüphanesi profilleri (5.5) ve zamanlanmış haftalık görevler (5.1).
/// </summary>
public partial class MainViewModel
{
    // ------------------------------------------------------------------
    // 5.5 Oyun kütüphaneleri
    // ------------------------------------------------------------------

    private void LoadGameLibraries()
    {
        GameLibraries.Clear();

        if (_gameLibraryService is null)
            return;

        foreach (var library in _gameLibraryService.DetectInstalledLibraries())
        {
            GameLibraries.Add(library);
        }

        if (GameLibraries.Count > 0)
        {
            LogActivity($"Kurulu oyun kütüphaneleri bulundu: {string.Join(", ", GameLibraries.Select(l => l.Name))}");
        }
    }

    private void ExecuteSelectGameLibrary(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!Directory.Exists(path))
        {
            LogActivity($"Oyun kütüphanesi yolu bulunamadı: {path}");
            return;
        }

        SelectedPath = path;
        StatusMessage = $"Oyun kütüphanesi seçildi: {path}";
        CurrentActionText = $"Taramak için 'TARA' butonuna tıklayın: {path}";
        LogActivity($"Oyun kütüphanesi seçildi: {path}");
    }

    // ------------------------------------------------------------------
    // 5.1 Zamanlanmış haftalık görev
    // ------------------------------------------------------------------

    /// <summary>
    /// Seçili klasör için Görev Zamanlayıcı'ya haftalık sıkıştırma görevi
    /// kaydeder (Pazartesi 03:00). Görev bu EXE'nin "cli compress" modunu
    /// çağırır; GUI'ye gerek kalmaz.
    /// </summary>
    public void ExecuteScheduleWeeklyTask()
    {
        if (IsBusy || _scheduledTaskService is null)
            return;

        if (!EnsureAlgorithmAllowed())
            return;

        if (string.IsNullOrWhiteSpace(SelectedPath) || !Directory.Exists(SelectedPath))
        {
            StatusMessage = "Zamanlanacak geçerli bir klasör seçin.";
            return;
        }

        var executablePath = Environment.ProcessPath ?? "WinCompressor.exe";
        string algorithmName = SelectedAlgorithm.ToString().ToLowerInvariant();
        string targetPath = SelectedPath.TrimEnd('\\');

        var definition = new ScheduledTaskDefinition(
            TaskName: BuildTaskName(targetPath),
            ExecutablePath: executablePath,
            Arguments: $"cli compress \"{targetPath}\" --algorithm {algorithmName}",
            DayOfWeek: DayOfWeek.Monday,
            Time: new TimeOnly(3, 0));

        if (_scheduledTaskService.TryCreateWeeklyTask(definition, out var error))
        {
            StatusMessage = $"Haftalık görev oluşturuldu: {targetPath} (Pazartesi 03:00, {SelectedAlgorithm}).";
            CurrentActionText = "Görev Zamanlayıcı → WinCompressor klasöründen yönetebilirsiniz.";
            LogActivity($"Zamanlanmış görev oluşturuldu: '{targetPath}' her Pazartesi 03:00'te {SelectedAlgorithm} ile sıkıştırılacak.");
        }
        else
        {
            StatusMessage = "Zamanlanmış görev oluşturulamadı.";
            LogError($"Zamanlanmış görev oluşturulamadı: {error}");
        }
    }

    /// <summary>Görev adı için yoldaki geçersiz karakterleri temizler.</summary>
    public static string BuildTaskName(string path)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(path.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "HaftalikSikistirma" : cleaned;
    }

    // ------------------------------------------------------------------
    // 5.4 Geri alma güvenlik ağı
    // ------------------------------------------------------------------

    /// <summary>
    /// Sıkıştırmadan ÖNCE dosyaların mevcut algoritmalarını kayda alır;
    /// işlem başarılı olursa geçmişe batch olarak eklenir.
    /// </summary>
    private static List<CompressionHistoryEntry> BuildHistoryEntries(
        IEnumerable<FileItemViewModel> items,
        CompressionAlgorithm newAlgorithm)
    {
        return items
            .Select(f => new CompressionHistoryEntry
            {
                FilePath = f.FilePath,
                PreviousAlgorithm = f.Model.CurrentAlgorithm.ToString(),
                NewAlgorithm = newAlgorithm.ToString()
            })
            .ToList();
    }

    private void RecordSuccessfulCompression(List<CompressionHistoryEntry> entries)
    {
        if (_historyService is null || entries.Count == 0)
            return;

        _historyService.AppendBatch(entries);
        RefreshRestoreCapability();
    }

    private void RefreshRestoreCapability()
    {
        bool canRestore = false;

        try
        {
            canRestore = _historyService is not null && _historyService.LoadLastBatch().Count > 0;
        }
        catch
        {
            canRestore = false;
        }

        CanRestoreLastOperation = canRestore;
    }

    /// <summary>
    /// Son toplu sıkıştırma işlemini geri alır: her dosya kaydedilen ESKİ
    /// algoritmasına döndürülür, ardından batch geçmişten düşürülür ve
    /// rapor günlüğe yazılır.
    /// </summary>
    public async Task ExecuteRestoreLastOperationAsync()
    {
        if (IsBusy || _historyService is null || !CanRestoreLastOperation)
            return;

        var batch = _historyService.LoadLastBatch();
        if (batch.Count == 0)
            return;

        IsCompressing = true;
        StatusMessage = $"Son işlem geri alınıyor ({batch.Count} dosya)...";
        ProgressPercentage = 0;
        ProgressText = $"0 / {batch.Count}";
        CurrentActionText = "Dosyalar önceki algoritmalarına döndürülüyor...";
        LogActivity($"Geri alma başlatıldı: son işlem ({batch.Count} dosya)");

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        var batchPaths = batch
            .Select(e => e.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var progress = new Progress<CompressionProgressReport>(report =>
        {
            ProgressPercentage = report.PercentComplete;
            ProgressText = $"{report.ProcessedFiles} / {report.TotalFiles}";
            CurrentActionText = $"Geri alınıyor: {Path.GetFileName(report.CurrentFilePath)}";
        });

        try
        {
            var revertService = new CompressionRevertService(_compressionService);
            var summary = await revertService.RevertBatchAsync(batch, progress, token);

            string batchId = batch[0].BatchId;

            if (summary.FailedCount == 0)
            {
                _historyService.RemoveBatch(batchId);
            }
            else
            {
                // K5 (inceleme bulgusu): kısmi geri almada batch'i koşulsuz silmek,
                // geri alınamayan dosyaların SON kurtarma kaydını da yok eder.
                // Başarılı girdiler düşürülür, başarısızlar yeni bir batch olarak
                // geçmişte korunur.
                var failedEntries = batch
                    .Where(e => summary.Items.Any(i =>
                        string.Equals(i.FilePath, e.FilePath, StringComparison.OrdinalIgnoreCase) &&
                        i.Status == "Failed"))
                    .Select(e => new CompressionHistoryEntry
                    {
                        FilePath = e.FilePath,
                        PreviousAlgorithm = e.PreviousAlgorithm,
                        NewAlgorithm = e.NewAlgorithm
                    })
                    .ToList();

                _historyService.RemoveBatch(batchId);

                if (failedEntries.Count > 0)
                {
                    _historyService.AppendBatch(failedEntries);
                    LogActivity($"Geri alınamayan {failedEntries.Count} dosyanın kurtarma kaydı geçmişte korundu.");
                }
            }

            // Revert yeni FileItemInfo örnekleri üzerinde çalışır; VM'in SAHİP
            // olduğu modelleri diskle uyumlu hale getir (yoksa IsCompressed bayatlar).
            var updatedByPath = summary.Items
                .Where(i => !string.IsNullOrEmpty(i.FilePath))
                .GroupBy(i => i.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (var fileVm in Files)
            {
                if (updatedByPath.TryGetValue(fileVm.FilePath, out var updated))
                {
                    var model = fileVm.Model;
                    model.AllocatedSize = updated.AllocatedSize;
                    model.IsCompressed = updated.IsCompressed;
                    model.CurrentAlgorithm = updated.CurrentAlgorithm;
                    model.Status = updated.Status;
                    model.ErrorMessage = updated.ErrorMessage;
                }
            }

            foreach (var fileVm in Files)
            {
                if (batchPaths.Contains(fileVm.FilePath))
                {
                    fileVm.Refresh();
                }
            }

            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            StatusMessage = $"Geri alma tamamlandı: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata.";
            CurrentActionText = summary.FailedCount == 0
                ? "Tüm dosyalar önceki durumuna döndü."
                : $"{summary.FailedCount} dosya geri alınamadı (günlüğe bakın).";
            ProgressPercentage = 100;
            LogActivity($"Geri alma bitti: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata.");

            if (summary.FailedCount > 0)
            {
                foreach (var item in summary.Items.Where(i => i.Status == "Failed"))
                {
                    LogError($"Geri alınamadı: {item.FileName} — {item.ErrorMessage}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Geri alma iptal edildi.";
            CurrentActionText = "Kullanıcı işlemi durdurdu.";
            LogActivity("Geri alma işlemi iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Geri alma hatası: {ex.Message}";
            CurrentActionText = "Geri alınırken hata oluştu.";
            LogError($"Geri alma hatası: {ex.Message}");
        }
        finally
        {
            ClearEstimates(Files.Where(f => batchPaths.Contains(f.FilePath)));
            _activeCts?.Dispose();
            _activeCts = null;
            IsCompressing = false;
            RefreshRestoreCapability();
        }
    }
}
