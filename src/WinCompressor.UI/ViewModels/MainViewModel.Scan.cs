using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.Converters;

namespace WinCompressor.UI.ViewModels;

/// <summary>
/// Tarama ile ilgili kısım: yol seçimi, tarama seçenekleri ve akış (streaming)
/// sonuç toplama.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Streaming batches awaiting adoption into the grid when no UI
    /// SynchronizationContext exists (unit tests): collected thread-safely and
    /// drained deterministically after the scan task completes. With a UI
    /// context (production), Progress&lt;T&gt; marshals batches to the UI thread and
    /// this queue stays empty.
    /// </summary>
    private readonly ConcurrentQueue<FileItemInfo> _pendingStreamedItems = new();
    public string SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (SetProperty(ref _selectedPath, value))
            {
                (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set => SetProperty(ref _includeSubdirectories, value);
    }

    /// <summary>
    /// When true, the AppData folder (excluded from scans by default together
    /// with $Recycle.Bin and system folders) is included in scan results.
    /// </summary>
    public bool IncludeAppData
    {
        get => _includeAppData;
        set => SetProperty(ref _includeAppData, value);
    }

    public bool OnlyUncompressed
    {
        get => _onlyUncompressed;
        set => SetProperty(ref _onlyUncompressed, value);
    }

    public void HandleDroppedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (Directory.Exists(path))
        {
            SelectedPath = path;
            LogActivity($"Sürükle-bırak ile klasör seçildi: {path}");
            StatusMessage = $"Klasör seçildi: {path}";
            CurrentActionText = $"Taramak için 'TARA' butonuna tıklayın: {path}";
        }
        else if (File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                SelectedPath = dir;
                LogActivity($"Sürükle-bırak ile dosyanın klasörü seçildi: {dir}");
                StatusMessage = $"Klasör seçildi: {dir}";
                CurrentActionText = $"Taramak için 'TARA' butonuna tıklayın: {dir}";
            }
        }
    }

    private void ExecuteBrowseFolder()
    {
        if (FolderPicker != null)
        {
            var chosen = FolderPicker();
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                SelectedPath = chosen;
            }
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Taranacak veya Sıkıştırılacak Klasörü Seçin",
            InitialDirectory = Directory.Exists(SelectedPath) ? SelectedPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedPath = dialog.FolderName;
        }
    }

    private void ExecuteQuickSelectDrive(string? driveParam)
    {
        if (string.IsNullOrWhiteSpace(driveParam))
            return;

        if (driveParam.Equals("USER", StringComparison.OrdinalIgnoreCase))
        {
            SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else
        {
            SelectedPath = driveParam;
        }
    }

    public async Task ExecuteScanAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(SelectedPath))
            return;

        IsScanning = true;
        StatusMessage = "Tarama başlatılıyor...";
        CurrentActionText = $"Klasör taranıyor: {SelectedPath}";
        ProgressPercentage = 0;
        ProgressText = "Taranıyor...";
        LogActivity($"Tarama başlatıldı: {SelectedPath} (Alt klasörler: {IncludeSubdirectories}, Sadece sıkıştırılmamış: {OnlyUncompressed}, AppData: {IncludeAppData})");

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        // Akış: liste baştan boşaltılır; sonuçlar tarama sürerken chunk'lar
        // halinde gelir. İptalde/şu ana kadar bulunanlar görünür kalır.
        Files.Clear();
        _scannedPaths.Clear();
        _pendingStreamedItems.Clear();
        TotalFilesFound = 0;
        TotalBytesFound = 0;
        OnListContentsChanged();

        var scanOptions = new ScanOptions
        {
            RootPath = SelectedPath,
            IncludeSubdirectories = IncludeSubdirectories,
            OnlyUncompressed = OnlyUncompressed,
            IncludeAppData = IncludeAppData
        };

        // UI bağlamı varsa Progress<T> batch'leri otomatik olarak UI iş
        // parçacığına taşır. Bağlam yoksa (unit test / arka plan) Files'a asla
        // yabancı iş parçacığından dokunulmaz: batch'ler kuyruğa alınır ve
        // await dönüşünde deterministik olarak boşaltılır.
        IProgress<ScanProgressReport> progress = SynchronizationContext.Current is null
            ? new QueuedScanProgress(this)
            : new Progress<ScanProgressReport>(OnScanProgressReport);

        try
        {
            var results = await _fileScanner.ScanAsync(scanOptions, progress, token);

            // Bağlamsız ortamda kuyruğa biriken batch'ler şimdi (doğru iş
            // parçacığında) grid'e eklenir; then final results eklenecek.
            if (!_pendingStreamedItems.IsEmpty)
            {
                var drained = new List<FileItemInfo>();
                while (_pendingStreamedItems.TryDequeue(out var pending))
                {
                    drained.Add(pending);
                }

                AddScannedFiles(drained);
            }

            // Kalan (son chunk'tan artan) dosyalar + WOF probe'u sonrası güncelleme.
            AddScannedFiles(results);

            foreach (var item in Files)
            {
                item.Refresh();
            }

            // INVARIANT: Files koleksiyonu katı biçimde boyuta göre azalandır.
            // Akış sırasında gelen batch'ler rastgele sırada eklendiğinden, tarama
            // bitiminde koleksiyon tek seferlik yeniden sıralanır (UI zaten
            // FilesView üzerinden sıralı gösterir; bu, model düzeyinde garanti).
            ReorderFilesBySizeDescending();

            TotalFilesFound = Files.Count;
            TotalBytesFound = Files.Sum(f => f.Size);
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);

            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            ApplyFilter();

            StatusMessage = $"Tarama tamamlandı: {TotalFilesFound} dosya bulundu ({ByteSizeConverter.Format(TotalBytesFound)}).";
            CurrentActionText = $"Seçili {SelectedFilesCount} dosya ({ByteSizeConverter.Format(SelectedFilesTotalSize)}) sıkıştırma için hazır.";
            ProgressPercentage = 100;
            ProgressText = $"{TotalFilesFound} dosya";
            LogActivity($"Tarama tamamlandı: {TotalFilesFound} dosya ({ByteSizeConverter.Format(TotalBytesFound)})");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Tarama kullanıcı tarafından iptal edildi. Listede {TotalFilesFound} dosya kaldı.";
            CurrentActionText = "Tarama durduruldu.";
            // T2-K3: akış sırasında bastırılan metrikler iptalde de güncellenir.
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            ApplyFilter();
            LogWarning($"Tarama iptal edildi — şu ana kadar bulunan {TotalFilesFound} dosya listede görünüyor.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Tarama hatası: {ex.Message}";
            CurrentActionText = "Tarama sırasında bir hata oluştu.";
            // T2-K3: akış sırasında bastırılan metrikler hata yolunda da güncellenir.
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            ApplyFilter();
            LogError($"Tarama hatası: {ex.Message}");
        }
        finally
        {
            _activeCts?.Dispose();
            _activeCts = null;
            IsScanning = false;
        }
    }

    /// <summary>
    /// Progress callback her zaman dispatcher thread — UI-marshal'lı akış yolu.
    /// Counter ve durum güncellemeleri + canlı batch ekleme yapar.
    /// </summary>
    private void OnScanProgressReport(ScanProgressReport report)
    {
        StatusMessage = $"Taranıyor: {report.ScannedFilesCount} dosya, {report.MatchedFilesCount} uygun dosya bulundu";
        CurrentActionText = string.IsNullOrWhiteSpace(report.CurrentDirectory)
            ? "Dizinler taranıyor..."
            : $"Taranan dizin: {report.CurrentDirectory}";
        // Live scan metrics (total size is unknown upfront, so the progress bar
        // runs indeterminate while these counters keep the user informed).
        ProgressText = $"⏳ {report.ScannedFilesCount} dosya • {report.MatchedFilesCount} uygun";

        // Streaming: tarayıcı, eşleşen dosyaları 500'lük chunk'larla
        // gönderir; UI tarama bitmeden dolmaya başlar.
        if (report.MatchedFilesBatch is { Count: > 0 })
        {
            AddScannedFiles(report.MatchedFilesBatch);
            ProgressText = $"⏳ {report.ScannedFilesCount} dosya • {TotalFilesFound} listelendi";
        }
    }

    /// <summary>
    /// SynchronizationContext'i olmayan ortamlar (unit test) için ilerleme
    /// adaptation'u: DataTable'a dokunmaz, yalnızca düz sayaç alanlarını
    /// güncelleme (<c>Progress&lt;T&gt;</c> kod parçacığına taşır).
    /// </summary>
    private sealed class QueuedScanProgress : IProgress<ScanProgressReport>
    {
        private readonly MainViewModel _owner;

        public QueuedScanProgress(MainViewModel owner)
        {
            _owner = owner;
        }

        public void Report(ScanProgressReport report)
        {
            // Plain property writes: no collection/view access, thread-safe.
            _owner.StatusMessage = $"Taranıyor: {report.ScannedFilesCount} dosya, {report.MatchedFilesCount} uygun dosya bulundu";
            _owner.CurrentActionText = string.IsNullOrWhiteSpace(report.CurrentDirectory)
                ? "Dizinler taranıyor..."
                : $"Taranan dizin: {report.CurrentDirectory}";
            _owner.ProgressText = $"⏳ {report.ScannedFilesCount} dosya • {report.MatchedFilesCount} uygun";

            if (report.MatchedFilesBatch is { Count: > 0 })
            {
                foreach (var item in report.MatchedFilesBatch)
                {
                    _owner._pendingStreamedItems.Enqueue(item);
                }
            }
        }
    }

    /// <summary>
    /// Streams matched files into the grid: deduplicates by path, applies the
    /// default selection (uncompressed files are pre-selected) and updates the
    /// cheap running counters. Heavy full-list metrics wait for the end of scan.
    /// </summary>
    private void AddScannedFiles(IEnumerable<FileItemInfo> models)
    {
        int added = 0;
        long addedBytes = 0;

        // T2-K3: akış sırasında öntanımlı seçim ataması madde başına tam liste
        // metrik taraması tetikliyordu (nesne başlatıcıdaki IsSelected, setter
        // üzerinden OnItemSelectionChanged'i çağırıyor). Bayrak, metrik
        // güncellemesini tarama sonuna erteler (o noktada tek geçiş yapılıyor).
        _isUpdatingSelectionAll = true;
        try
        {
            foreach (var model in models)
            {
                if (string.IsNullOrWhiteSpace(model.FilePath) || !_scannedPaths.Add(model.FilePath))
                    continue;

                var vm = new FileItemViewModel(model, OnItemSelectionChanged)
                {
                    IsSelected = !model.IsCompressed
                };
                Files.Add(vm);
                added++;
                addedBytes += model.Size;
            }
        }
        finally
        {
            _isUpdatingSelectionAll = false;
        }

        if (added > 0)
        {
            TotalFilesFound = Files.Count;
            TotalBytesFound += addedBytes;
        }
    }

    /// <summary>
    /// Restores the strictly-descending-by-size ordering of <see cref="Files"/>
    /// after streaming added batches in scan order. Performs a single
    /// reset-add cycle only when the order actually differs, keeping UI churn
    /// minimal on big lists.
    /// </summary>
    private void ReorderFilesBySizeDescending()
    {
        // Guard (inceleme bulgusu K2): hiç dosya eşleşmeyen taramada (boş klasör,
        // <4KB, filtreli) ordered[0] erişimi ArgumentOutOfRangeException fırlatıyordu
        // ve tarama "Tarama hatası: Index was outside..." ile bitiyordu.
        if (Files.Count == 0)
            return;

        var ordered = Files.OrderByDescending(f => f.Size).ToList();

        bool alreadySorted = ReferenceEquals(ordered[0], Files[0]);
        for (int i = 1; i < ordered.Count && alreadySorted; i++)
        {
            alreadySorted = ReferenceEquals(ordered[i], Files[i]);
        }

        if (alreadySorted)
            return;

        Files.Clear();
        foreach (var item in ordered)
        {
            Files.Add(item);
        }
    }

    /// <summary>Resets selection state when the list is emptied before a new scan.</summary>
    private void OnListContentsChanged()
    {
        UpdateSelectionMetrics();
        UpdateStorageMetrics();
        ApplyFilter();
    }
}
