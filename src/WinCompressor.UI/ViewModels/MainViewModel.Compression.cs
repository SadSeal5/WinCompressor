using System.IO;
using WinCompressor.Core.Models;
using WinCompressor.UI.Converters;

namespace WinCompressor.UI.ViewModels;

/// <summary>
/// Sıkıştırma / sıkıştırma kaldırma / simülasyon işlemleri kısmı.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// K4: toplu (de)sıkıştırma progress'inde metriklerin ne sıklıkla
    /// yeniden hesaplanacağı (tam-liste rescan, dosya başına değil).
    /// </summary>
    private const int ProgressMetricsBatchSize = 25;

    /// <summary>
    /// Guard: CompactOS WOF algorithms require admin rights. Refuses the operation
    /// upfront with a clear message instead of failing every file at compact.exe time.
    /// </summary>
    private bool EnsureAlgorithmAllowed()
    {
        if (CanUseSelectedAlgorithm)
            return true;

        StatusMessage = "Seçilen algoritma yönetici yetkisi gerektiriyor. NTFS seçin veya uygulamayı yönetici olarak yeniden başlatın.";
        CurrentActionText = "İşlem reddedildi: algoritma yönetici yetkisi gerektiriyor.";
        LogActivity($"İşlem reddedildi: {SelectedAlgorithm} algoritması yönetici yetkisi gerektiriyor.");
        return false;
    }

    public async Task ExecuteCompressSelectedAsync()
    {
        if (IsBusy)
            return;

        var selectedItems = Files.Where(f => f.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            StatusMessage = "Lütfen sıkıştırılacak en az bir dosya seçin.";
            return;
        }

        if (!EnsureAlgorithmAllowed())
            return;

        IsCompressing = true;
        StatusMessage = $"{selectedItems.Count} dosya sıkıştırılıyor ({SelectedAlgorithm})...";
        ProgressPercentage = 0;
        ProgressText = $"0 / {selectedItems.Count}";
        LogActivity($"Toplu sıkıştırma başlatıldı: {selectedItems.Count} dosya ({SelectedAlgorithm})");

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        var fileMap = selectedItems.ToDictionary(k => k.FilePath, StringComparer.OrdinalIgnoreCase);

        // K4 (inceleme bulgusu): servis dosya başına rapor basıyor; her raporda
        // Files.Sum + metrik güncellemeleri dosya başına ~5 tam O(n) geçiş
        // demekti — 100k dosyada UI kilidi. Metrikler artık yalnız her
        // ProgressMetricsBatchSize raporda ve son raporda hesaplanır.
        int metricsSinceLastBatch = 0;

        var progress = new Progress<CompressionProgressReport>(report =>
        {
            ProgressPercentage = report.PercentComplete;
            ProgressText = $"{report.ProcessedFiles} / {report.TotalFiles} (%{report.PercentComplete:0.0})";
            CurrentActionText = $"Sıkıştırılıyor: {Path.GetFileName(report.CurrentFilePath)} ({ByteSizeConverter.Format(report.TotalBytesSaved)} kazanıldı)";

            if (!string.IsNullOrEmpty(report.CurrentFilePath) && fileMap.TryGetValue(report.CurrentFilePath, out var currentVm))
            {
                currentVm.Refresh();
            }

            metricsSinceLastBatch++;
            if (metricsSinceLastBatch >= ProgressMetricsBatchSize || report.ProcessedFiles >= report.TotalFiles)
            {
                metricsSinceLastBatch = 0;
                TotalBytesSaved = Files.Sum(f => f.BytesSaved);
                UpdateSelectionMetrics();
                UpdateStorageMetrics();
            }
        });

        // 5.4: işlemden ÖNCE eski algoritmalar kayda alınıyor (modeller üzerinde
        // sıkıştırma algoritmayı güncellediği için önce okunmalı).
        var historyEntries = BuildHistoryEntries(selectedItems, SelectedAlgorithm);

        // T2-K2 (inceleme turu 2): Status kalıcıdır — batch'e seçilmiş ama
        // batch ona ulaşmadan İPTAL edilen, ÖNCEDEN sıkışmış bir dosya için
        // sahte geri alma kaydı üretilmesin diye batch öncesi durum ayrıca
        // alınır. Tamamlanma yolunda bu dosyalar işlenir (servis başarılı
        // döner), o yüzden filtre yalnızca iptal dalında kullanılır.
        // T3-N1 (inceleme turu 3): except yalnız "algoritması da
        // DEĞİŞMEMİŞ" önceden-sıkışmışlara uygulanır — batch bu dosyayı
        // farklı algoritmaya geçirdiyse gerçek bir değişimdir, kaydedilir.
        var previousAlgorithms = selectedItems
            .Where(i => i.Model.Status == "Compressed")
            .ToDictionary(i => i.FilePath, i => i.Model.CurrentAlgorithm);

        try
        {
            var targetModels = selectedItems.Select(vm => vm.Model).ToList();

            var summary = await _compressionService.CompressFilesAsync(
                targetModels,
                SelectedAlgorithm,
                maxDegreeOfParallelism: CompressionParallelism,
                progress: progress,
                cancellationToken: token);

            foreach (var item in selectedItems)
            {
                item.Refresh();
            }

            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            if (summary.SuccessfulCount > 0)
            {
                // K5: yalnız fiilen sıkışan (Status=="Compressed") dosyalar
                // geçmişe yazılır — başarısızlar revert'te sahte "başarı" olmasın.
                RecordSuccessfulCompression(FilterCompressedEntries(historyEntries, selectedItems));
            }

            StatusMessage = $"Sıkıştırma tamamlandı: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata.";
            CurrentActionText = $"İşlem bitti. Toplam tasarruf: {ByteSizeConverter.Format(summary.TotalBytesSaved)}.";
            ProgressPercentage = 100;
            LogActivity($"Toplu sıkıştırma bitti: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata. Toplam kazanç: {ByteSizeConverter.Format(summary.TotalBytesSaved)}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sıkıştırma işlemi iptal edildi.";
            CurrentActionText = "Kullanıcı işlemi durdurdu.";
            foreach (var item in selectedItems)
            {
                item.Refresh();
            }

            // K5: iptal, en çok geri alma kaydının gerektiği andır. İptalden önce
            // fiilen sıkışmış dosyalar için de kısmi bir batch kaydı yazılır.
            // T2-K2: "önceden sıkışmış ama bu batch'te işlenmemiş" dosyalar
            // hariç tutulur (sahte kayıt üretmesin).
            // T3-N1: batch'te algoritması DEĞİŞMİŞ önceden-sıkışmış dosya gerçek
            // bir değişim geçirdiğinden kayda ALINIR; yalnız dokunulmamışlar hariçtir.
            var unchangedSinceBatchStart = selectedItems
                .Where(i => previousAlgorithms.TryGetValue(i.FilePath, out var before)
                         && i.Model.Status == "Compressed"
                         && i.Model.CurrentAlgorithm == before)
                .Select(i => i.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var compressedBeforeCancel = FilterCompressedEntries(historyEntries, selectedItems, unchangedSinceBatchStart);
            if (compressedBeforeCancel.Count > 0)
            {
                RecordSuccessfulCompression(compressedBeforeCancel);
                LogActivity($"İptal: {compressedBeforeCancel.Count} dosya sıkışmıştı — geri alınabilir kayıt oluşturuldu.");
            }

            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            LogActivity("Sıkıştırma işlemi iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sıkıştırma hatası: {ex.Message}";
            CurrentActionText = "Hata oluştu.";
            LogError($"Sıkıştırma sırasında hata: {ex.Message}");
        }
        finally
        {
            ClearEstimates(selectedItems);
            _activeCts?.Dispose();
            _activeCts = null;
            IsCompressing = false;
        }
    }

    public async Task ExecuteDecompressSelectedAsync()
    {
        if (IsBusy)
            return;

        var selectedItems = Files.Where(f => f.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            StatusMessage = "Lütfen sıkıştırması kaldırılacak en az bir dosya seçin.";
            return;
        }

        IsCompressing = true;
        StatusMessage = $"{selectedItems.Count} dosyanın sıkıştırması kaldırılıyor...";
        ProgressPercentage = 0;
        ProgressText = $"0 / {selectedItems.Count}";
        LogActivity($"Sıkıştırma kaldırma başlatıldı: {selectedItems.Count} dosya");

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        var fileMap = selectedItems.ToDictionary(k => k.FilePath, StringComparer.OrdinalIgnoreCase);

        // K4: dosya başına tam-liste rescan yerine toplu güncelleme (yukarıya bkz.).
        int metricsSinceLastBatch = 0;

        var progress = new Progress<CompressionProgressReport>(report =>
        {
            ProgressPercentage = report.PercentComplete;
            ProgressText = $"{report.ProcessedFiles} / {report.TotalFiles} (%{report.PercentComplete:0.0})";
            CurrentActionText = $"Sıkıştırma kaldırılıyor: {Path.GetFileName(report.CurrentFilePath)}";

            if (!string.IsNullOrEmpty(report.CurrentFilePath) && fileMap.TryGetValue(report.CurrentFilePath, out var currentVm))
            {
                currentVm.Refresh();
            }

            metricsSinceLastBatch++;
            if (metricsSinceLastBatch >= ProgressMetricsBatchSize || report.ProcessedFiles >= report.TotalFiles)
            {
                metricsSinceLastBatch = 0;
                TotalBytesSaved = Files.Sum(f => f.BytesSaved);
                UpdateSelectionMetrics();
                UpdateStorageMetrics();
            }
        });

        try
        {
            var targetModels = selectedItems.Select(vm => vm.Model).ToList();
            var summary = await _compressionService.DecompressFilesAsync(
                targetModels,
                maxDegreeOfParallelism: CompressionParallelism,
                progress: progress,
                cancellationToken: token);

            foreach (var item in selectedItems)
            {
                item.Refresh();
            }

            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            // T4-M1 (inceleme turu 4): toplu sıkıştırma-kaldırma, kullanıcı adına
            // TAMAMLANMIŞ bir geri alma işlemidir — açılan dosyaların geçmiş
            // girdileri düşürülür; yoksa "Son İşlemi Geri Al" bayat batch'i
            // uygulayıp az önce açılan dosyaları fark ettirmeden yeniden sıkıştırır.
            if (_historyService is not null && summary.SuccessfulCount > 0)
            {
                var decompressedPaths = summary.Items
                    .Where(i => i.Status == "Decompressed")
                    .Select(i => i.FilePath)
                    .ToList();

                if (decompressedPaths.Count > 0)
                {
                    _historyService.RemoveEntries(decompressedPaths);
                    RefreshRestoreCapability();
                    LogActivity($"Açılan {decompressedPaths.Count} dosyanın geri alma kaydı geçmişten düşürüldü.");
                }
            }

            StatusMessage = $"Sıkıştırma kaldırma tamamlandı: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata.";
            CurrentActionText = "Dosyalar orijinal boyutlarına döndürüldü.";
            ProgressPercentage = 100;
            LogActivity($"Sıkıştırma kaldırma bitti: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sıkıştırma kaldırma işlemi iptal edildi.";
            CurrentActionText = "Kullanıcı işlemi durdurdu.";
            foreach (var item in selectedItems)
            {
                item.Refresh();
            }
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            // T5-M2 (inceleme turu 5): iptal anına kadar AÇILMIŞ dosyaların
            // geçmiş girdileri de düşürülmeli — T4-M1'in kapattığı bayat-geçmiş
            // sınıfı iptal yolunda yaşıyordu ("Son İşlemi Geri Al" az önce
            // açılan dosyaları yeniden sıkıştırabiliyordu).
            if (_historyService is not null)
            {
                var decompressedPaths = selectedItems
                    .Where(i => i.Model.Status == "Decompressed")
                    .Select(i => i.FilePath)
                    .ToList();

                if (decompressedPaths.Count > 0)
                {
                    _historyService.RemoveEntries(decompressedPaths);
                    RefreshRestoreCapability();
                    LogActivity($"İptalde açılan {decompressedPaths.Count} dosyanın geri alma kaydı geçmişten düşürüldü.");
                }
            }

            LogActivity("Sıkıştırma kaldırma iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
            CurrentActionText = "Sıkıştırma kaldırılırken hata oluştu.";
            LogError($"Sıkıştırma kaldırma hatası: {ex.Message}");
        }
        finally
        {
            ClearEstimates(selectedItems);
            _activeCts?.Dispose();
            _activeCts = null;
            IsCompressing = false;
        }
    }

    /// <summary>
    /// Dry-run: estimates per-file savings for the selected algorithm WITHOUT
    /// touching any file. Results appear in the "Tahmini Tasarruf" column and
    /// are cleared once real (de)compression happens.
    /// </summary>
    public async Task ExecuteSimulateSelectedAsync()
    {
        if (IsBusy)
            return;

        var selectedItems = Files.Where(f => f.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            StatusMessage = "Lütfen simüle edilecek en az bir dosya seçin.";
            return;
        }

        IsCompressing = true;
        StatusMessage = $"{selectedItems.Count} dosya simüle ediliyor ({SelectedAlgorithm})...";
        ProgressPercentage = 0;
        ProgressText = $"0 / {selectedItems.Count}";
        CurrentActionText = "Sıkıştırmadan önce tahmini tasarruf hesaplanıyor...";
        LogActivity($"Simülasyon başlatıldı: {selectedItems.Count} dosya ({SelectedAlgorithm})");

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        int processedCount = 0;

        // K6 (inceleme bulgusu): VM progress skalarları doğrudan Parallel.ForEachAsync
        // worker thread'lerinden yazılıyordu; WPF skalar binding'leri bunu genelde
        // tolere etse de tasarım tutarlılığı açısından bildirimler Progress<T>
        // üzerinden geçer (üretimde dispatcher'a marshal olur). Not: tekil dosya
        // modelindeki güncelleme (EstimatedSavedBytes + Refresh) worker'da senkron
        // kalır — await dönüşünde dosya başına tahmin eksiksiz uygulanmış olur.
        IProgress<(FileItemViewModel Item, int Current)> progress =
            new Progress<(FileItemViewModel Item, int Current)>(update =>
        {
            ProgressPercentage = (double)update.Current / selectedItems.Count * 100.0;
            ProgressText = $"{update.Current} / {selectedItems.Count}";
            CurrentActionText = $"Simüle ediliyor: {update.Item.FileName}";
        });

        try
        {
            await Parallel.ForEachAsync(
                selectedItems,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = CompressionParallelism,
                    CancellationToken = token
                },
                async (fileVm, ct) =>
                {
                    long estimated = await _estimationService.EstimateSavedBytesAsync(
                        fileVm.FilePath, fileVm.Size, SelectedAlgorithm, ct);

                    fileVm.Model.EstimatedSavedBytes = estimated;
                    fileVm.Refresh();

                    int current = Interlocked.Increment(ref processedCount);
                    progress.Report((fileVm, current));
                });

            long totalEstimated = selectedItems.Sum(f => f.Model.EstimatedSavedBytes ?? 0);

            StatusMessage = $"Simülasyon tamamlandı: {selectedItems.Count} dosya için tahmini ~{ByteSizeConverter.Format(totalEstimated)} tasarruf ({SelectedAlgorithm}).";
            CurrentActionText = $"Tahmini tasarruf: ~{ByteSizeConverter.Format(totalEstimated)} — sonuçlar 'Tahmini Tasarruf' kolonunda.";
            ProgressPercentage = 100;
            ProgressText = $"{selectedItems.Count} dosya";
            LogActivity($"Simülasyon bitti: {selectedItems.Count} dosya, {SelectedAlgorithm} algoritmasıyla tahmini ~{ByteSizeConverter.Format(totalEstimated)} tasarruf.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Simülasyon iptal edildi.";
            CurrentActionText = "Kullanıcı işlemi durdurdu.";
            LogActivity("Simülasyon işlemi iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Simülasyon hatası: {ex.Message}";
            CurrentActionText = "Simülasyon sırasında bir hata oluştu.";
            LogError($"Simülasyon sırasında hata: {ex.Message}");
        }
        finally
        {
            _activeCts?.Dispose();
            _activeCts = null;
            IsCompressing = false;
        }
    }

    public async Task ExecuteCompressSingleFileAsync(FileItemViewModel? item)
    {
        if (IsBusy || item == null)
            return;

        if (!EnsureAlgorithmAllowed())
            return;

        IsCompressing = true;
        StatusMessage = $"{item.FileName} sıkıştırılıyor ({SelectedAlgorithm})...";
        CurrentActionText = $"Sıkıştırılıyor: {item.FileName}";
        LogActivity($"Tekil dosya sıkıştırma başlatıldı: {item.FileName} ({SelectedAlgorithm})");

        // T4-N3 (inceleme turu 4): tekil işlemde de İptal butonu görünür durumda
        // olduğundan _activeCts kurulumu yapılır; yoksa buton sessiz no-op'tu.
        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        // 5.4: eski algoritma işlemden önce kaydedilir.
        string previousAlgorithm = item.Model.CurrentAlgorithm.ToString();

        try
        {
            bool success = await _compressionService.CompressFileAsync(item.Model, SelectedAlgorithm, token);
            item.Refresh();
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            if (success)
            {
                RecordSuccessfulCompression(
                [
                    new CompressionHistoryEntry
                    {
                        FilePath = item.FilePath,
                        PreviousAlgorithm = previousAlgorithm,
                        NewAlgorithm = SelectedAlgorithm.ToString()
                    }
                ]);

                StatusMessage = $"{item.FileName} başarıyla sıkıştırıldı.";
                CurrentActionText = $"Tasarruf: {ByteSizeConverter.Format(item.BytesSaved)}.";
                LogActivity($"Sıkıştırma tamamlandı: {item.FileName} ({ByteSizeConverter.Format(item.BytesSaved)} tasarruf)");
            }
            else
            {
                StatusMessage = $"{item.FileName} sıkıştırılamadı.";
                LogError($"Sıkıştırma başarısız: {item.FileName} - {item.ErrorMessage ?? "Hata"}");
            }
        }
        catch (OperationCanceledException)
        {
            // T5-M1 (inceleme turu 5): iptal hata DEĞİL — toplu işlemlerle aynı
            // ayrıcalıklı dal; yoksa kullanıcı "Hata: The operation was canceled."
            // görüyor ve bilinçli iptal günlüğe hata olarak yazılıyordu.
            item.Refresh();
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            StatusMessage = $"{item.FileName} sıkıştırması iptal edildi.";
            CurrentActionText = "Kullanıcı işlemi durdurdu.";
            LogActivity($"Tekil sıkıştırma iptal edildi: {item.FileName}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
            LogError($"Sıkıştırma hatası ({item.FileName}): {ex.Message}");
        }
        finally
        {
            ClearEstimates([item]);
            _activeCts?.Dispose();
            _activeCts = null;
            IsCompressing = false;
        }
    }

    public async Task ExecuteDecompressSingleFileAsync(FileItemViewModel? item)
    {
        if (IsBusy || item == null)
            return;

        IsCompressing = true;
        StatusMessage = $"{item.FileName} sıkıştırması kaldırılıyor...";
        CurrentActionText = $"Sıkıştırma kaldırılıyor: {item.FileName}";
        LogActivity($"Tekil dosya sıkıştırma kaldırma: {item.FileName}");

        // T4-N3: tekil işlemde de İptal aktif (toplu işlem ile aynı desen).
        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        try
        {
            bool success = await _compressionService.DecompressFileAsync(item.Model, token);
            item.Refresh();
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            if (success)
            {
                // T4-M1 tutarlılığı: tekil açılan dosyanın geri alma kaydı da düşülür
                // (toplu decompress ile aynı bayat-geçmiş kuralı).
                if (_historyService is not null && item.Model.Status == "Decompressed")
                {
                    _historyService.RemoveEntries([item.FilePath]);
                    RefreshRestoreCapability();
                }

                StatusMessage = $"{item.FileName} orijinal boyutuna döndürüldü.";
                CurrentActionText = "Sıkıştırma kaldırıldı.";
                LogActivity($"Sıkıştırma kaldırıldı: {item.FileName}");
            }
            else
            {
                StatusMessage = $"{item.FileName} sıkıştırması kaldırılamadı.";
                LogError($"Sıkıştırma kaldırma başarısız: {item.FileName} - {item.ErrorMessage ?? "Hata"}");
            }
        }
        catch (OperationCanceledException)
        {
            // T5-M1 (inceleme turu 5): iptal hata değil — tekil compress ile aynı
            // ayrıcalıklı dal. İptal anında dosya açılmışsa geçmiş girdisi de
            // düşürülür (T4-M1 bayat-geçmiş kuralının iptal yolu).
            item.Refresh();
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            if (_historyService is not null && item.Model.Status == "Decompressed")
            {
                _historyService.RemoveEntries([item.FilePath]);
                RefreshRestoreCapability();
            }

            StatusMessage = $"{item.FileName} sıkıştırması kaldırma iptal edildi.";
            CurrentActionText = "Kullanıcı işlemi durdurdu.";
            LogActivity($"Tekil sıkıştırma kaldırma iptal edildi: {item.FileName}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
            LogError($"Sıkıştırma kaldırma hatası ({item.FileName}): {ex.Message}");
        }
        finally
        {
            ClearEstimates([item]);
            _activeCts?.Dispose();
            _activeCts = null;
            IsCompressing = false;
        }
    }

    /// <summary>
    /// K5 (inceleme bulgusu): geçmiş kaydına yalnız fiilen sıkışmış dosyaların
    /// girdileri alınır; sıkıştırılamayan dosyalar revert'te sahte başarı
    /// üretmesin.
    /// T2-K2 (inceleme turu 2): <paramref name="exceptPaths"/> kümesindeki
    /// dosyalar bu batch'te işlenmedi sayılır (iptal yolu) ve kayda alınmaz.
    /// </summary>
    private static List<CompressionHistoryEntry> FilterCompressedEntries(
        List<CompressionHistoryEntry> entries,
        IReadOnlyList<FileItemViewModel> items,
        HashSet<string>? exceptPaths = null)
    {
        var compressedPaths = items
            .Where(i => i.Model.Status == "Compressed")
            .Select(i => i.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return entries
            .Where(e => compressedPaths.Contains(e.FilePath))
            .Where(e => exceptPaths is null || !exceptPaths.Contains(e.FilePath))
            .ToList();
    }

    /// <summary>
    /// Dry-run estimates become stale the moment real compression state changes;
    /// drop them so the grid never shows outdated predictions next to real numbers.
    /// </summary>
    private static void ClearEstimates(IEnumerable<FileItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item.Model.EstimatedSavedBytes is not null)
            {
                item.Model.EstimatedSavedBytes = null;
                item.Refresh();
            }
        }
    }

    private void ExecuteCancel()
    {
        if (IsBusy && _activeCts != null && !_activeCts.IsCancellationRequested)
        {
            StatusMessage = "İşlem iptal ediliyor...";
            CurrentActionText = "Lütfen bekleyin...";
            _activeCts.Cancel();
            LogActivity("İptal isteği gönderildi.");
        }
    }

    private void ExecuteRestartAsAdmin()
    {
        LogActivity("Yönetici olarak yeniden başlatılıyor...");
        _adminService.RestartAsAdmin();
    }
}
