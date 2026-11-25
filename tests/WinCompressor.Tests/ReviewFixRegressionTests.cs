using System.Globalization;
using System.IO;
using System.Windows;
using WinCompressor.Core.Models;
using WinCompressor.Core.Cli;
using WinCompressor.Core.Services;
using WinCompressor.UI;
using WinCompressor.UI.Converters;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

/// <summary>
/// İnceleme (Goldfish tur 1) düzeltmelerinin regresyon testleri:
/// K1 compact hata çıktısı simülasyonu (CompressionService seam'i),
/// K2 boş tarama sonucu çökmesi, K3 dil ayarı gidiş-dönüşü,
/// K5 geri alma geçmişi boşlukları (başarısızlar, iptal, kısmi revert).
/// </summary>
public class ReviewFixRegressionTests
{
    private class FakeAdmin : IAdminService
    {
        public bool IsAdministrator() => false;
        public bool RestartAsAdmin(string? arguments = null) => true;
    }

    private class FakeScanner : IFileScannerService
    {
        public List<FileItemInfo> ResultsToReturn { get; set; } = [];

        public Task<IReadOnlyList<FileItemInfo>> ScanAsync(
            ScanOptions options,
            IProgress<ScanProgressReport>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FileItemInfo>>(ResultsToReturn);

        public bool IsRootDrive(string path) => false;
    }

    /// <summary>
    /// T2-K2: batch'teki hiçbir dosyaya dokunmadan iptal olan sahte servis.
    /// (Gerçek: CompressionService iptal edilirse o ana kadar işlenenler için
    /// kısmi özet dönmeyebilir; VM iptal dalı bu duruma düşmelidir.)
    /// </summary>
    private sealed class CancellingCompressionService : ICompressionService
    {
        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(new OperationCanceledException(cancellationToken));

        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(new OperationCanceledException(cancellationToken));

        public Task<CompressionResultSummary> CompressFilesAsync(
            IEnumerable<FileItemInfo> files, CompressionAlgorithm algorithm, int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromException<CompressionResultSummary>(new OperationCanceledException(cancellationToken));

        public Task<CompressionResultSummary> DecompressFilesAsync(
            IEnumerable<FileItemInfo> files, int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromException<CompressionResultSummary>(new OperationCanceledException(cancellationToken));
    }

    /// <summary>Belirtilen yollarda başarısız olan sahte sıkıştırma servisi.</summary>
    private class SelectiveCompressionService : ICompressionService
    {
        private readonly HashSet<string> _failPaths;
        private HashSet<string> _decompressFailPaths = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _cancelOnCompressPaths = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _cancelOnDecompressPaths = new(StringComparer.OrdinalIgnoreCase);
        private int? _cancelDecompressAfter;
        private bool _cancelAfterFirstSuccess;

        public int CompressCalls;
        public int DecompressCalls;

        public SelectiveCompressionService(IEnumerable<string> failPaths, bool cancelAfterFirstSuccess = false)
        {
            _failPaths = new HashSet<string>(failPaths, StringComparer.OrdinalIgnoreCase);
            _cancelAfterFirstSuccess = cancelAfterFirstSuccess;
        }

        /// <summary>T4M1 testi: bu yoldaki dosya yalnız DECOMPRESS'te başarısız olur.</summary>
        public void FailOnDecompress(string path) => _decompressFailPaths.Add(path);

        /// <summary>T5M1 testi: bu yol tekil COMPRESS'te OCE fırlatır (İptal simülasyonu).</summary>
        public void CancelOnCompress(string path) => _cancelOnCompressPaths.Add(path);

        /// <summary>T5M1 testi: bu yol tekil DECOMPRESS'te OCE fırlatır.</summary>
        public void CancelOnDecompress(string path) => _cancelOnDecompressPaths.Add(path);

        /// <summary>T5M2 testi: toplu decompress'te N başarından sonra OCE fırlatır.</summary>
        public void CancelDecompressAfter(int successCount) => _cancelDecompressAfter = successCount;

        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default)
        {
            CompressCalls++;
            if (_cancelOnCompressPaths.Contains(fileItem.FilePath))
                return Task.FromException<bool>(new OperationCanceledException(cancellationToken));

            if (_failPaths.Contains(fileItem.FilePath))
            {
                fileItem.Status = "Failed";
                fileItem.ErrorMessage = "simüle hata";
                return Task.FromResult(false);
            }

            if (_cancelAfterFirstSuccess)
            {
                _cancelAfterFirstSuccess = false;
                fileItem.Status = "Compressed";
                fileItem.CurrentAlgorithm = algorithm;
                return Task.FromException<bool>(new OperationCanceledException());
            }

            fileItem.Status = "Compressed";
            fileItem.CurrentAlgorithm = algorithm;
            fileItem.IsCompressed = true;
            return Task.FromResult(true);
        }

        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default)
        {
            DecompressCalls++;
            if (_cancelOnDecompressPaths.Contains(fileItem.FilePath))
                return Task.FromException<bool>(new OperationCanceledException(cancellationToken));

            if (_failPaths.Contains(fileItem.FilePath) || _decompressFailPaths.Contains(fileItem.FilePath))
            {
                fileItem.Status = "Failed";
                fileItem.ErrorMessage = "simüle hata";
                return Task.FromResult(false);
            }

            fileItem.Status = "Decompressed";
            fileItem.CurrentAlgorithm = CompressionAlgorithm.None;
            fileItem.IsCompressed = false;
            return Task.FromResult(true);
        }

        public async Task<CompressionResultSummary> CompressFilesAsync(
            IEnumerable<FileItemInfo> files, CompressionAlgorithm algorithm, int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null, CancellationToken cancellationToken = default)
        {
            var items = files.ToList();
            int success = 0, failed = 0;

            foreach (var file in items)
            {
                if (await CompressFileAsync(file, algorithm, cancellationToken))
                    success++;
                else
                    failed++;
            }

            return new CompressionResultSummary
            {
                TotalFiles = items.Count,
                SuccessfulCount = success,
                FailedCount = failed,
                Items = items
            };
        }

        public async Task<CompressionResultSummary> DecompressFilesAsync(
            IEnumerable<FileItemInfo> files, int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null, CancellationToken cancellationToken = default)
        {
            var items = files.ToList();
            int success = 0, failed = 0;

            foreach (var file in items)
            {
                if (await DecompressFileAsync(file, cancellationToken))
                {
                    success++;

                    // T5M2 testi: iptal anına kadar AÇILMIŞ dosyalar gerçek
                    // serviste olduğu gibi modellerde "Decompressed" kalır.
                    if (_cancelDecompressAfter == success)
                        throw new OperationCanceledException(cancellationToken);
                }
                else
                    failed++;
            }

            return new CompressionResultSummary
            {
                TotalFiles = items.Count,
                SuccessfulCount = success,
                FailedCount = failed,
                Items = items
            };
        }
    }

    private static string NewTempFolder() =>
        Path.Combine(Path.GetTempPath(), "wc_review_fix_" + Guid.NewGuid().ToString("N"));

    // ------------------------------------------------------------------
    // K1: compact hata çıktısı simülasyonu (CompressionService seam'i)
    // ------------------------------------------------------------------

    private static Func<string[], CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>>? StepRunner(
        (int Code, string Out, string Err) step1,
        (int Code, string Out, string Err) step2) =>
        (args, ct) =>
        {
            bool isStep1 = args.Length > 1 && args[0] == "/u" && args[1] == "/exe";
            var result = isStep1 ? step1 : step2;
            return Task.FromResult((ExitCode: result.Code, StdOut: result.Out, StdErr: result.Err));
        };

    [Fact]
    public async Task K1_Compress_NonZeroExitCode_MarksFailed()
    {
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("k1_a.dat", 8192);

        var service = new CompressionService((args, ct) =>
            Task.FromResult((ExitCode: 1, StdOut: "", StdErr: "")));

        var item = new FileItemInfo { FilePath = path, FileName = "k1_a.dat", Size = 8192, AllocatedSize = 8192 };
        bool success = await service.CompressFileAsync(item, CompressionAlgorithm.Ntfs);

        Assert.False(success);
        Assert.Equal("Failed", item.Status);
    }

    [Fact]
    public async Task K1_Compress_ErrorMarkerInStdOut_MarksFailed()
    {
        // Faz 1.2 regresyonu: exit code 0 OLSA BİLE stdout'ta [ERR] varsa
        // başarısız sayılmalı (|| mantığı).
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("k1_b.dat", 8192);

        var service = new CompressionService((args, ct) =>
            Task.FromResult((ExitCode: 0, StdOut: "compact [ERR] error occurred", StdErr: "")));

        var item = new FileItemInfo { FilePath = path, FileName = "k1_b.dat", Size = 8192, AllocatedSize = 8192 };
        bool success = await service.CompressFileAsync(item, CompressionAlgorithm.Ntfs);

        Assert.False(success);
        Assert.Equal("Failed", item.Status);
    }

    [Fact]
    public async Task K1_Decompress_Step1FailsButStep2Succeeds_StillSucceeds()
    {
        // Faz 1.2: adım-1 non-fatal — WOF-sıkıştırılmamış dosyada compact.exe
        // adım-1'de hata basar; adım-2 başarılıysa işlem başarılıdır.
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("k1_c.dat", 8192);

        var service = new CompressionService(StepRunner(
            (1, "[ERR] file is not WOF compressed", ""),
            (0, "", "")));

        var item = new FileItemInfo { FilePath = path, FileName = "k1_c.dat", Size = 8192, AllocatedSize = 4096 };
        bool success = await service.DecompressFileAsync(item);

        Assert.True(success);
        Assert.Equal("Decompressed", item.Status);
    }

    [Fact]
    public async Task K1_Decompress_Step2FailExitCode_MarksFailed()
    {
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("k1_d.dat", 8192);

        var service = new CompressionService(StepRunner(
            (0, "", ""),
            (1, "", "access denied")));

        var item = new FileItemInfo { FilePath = path, FileName = "k1_d.dat", Size = 8192, AllocatedSize = 4096 };
        bool success = await service.DecompressFileAsync(item);

        Assert.False(success);
        Assert.Equal("Failed", item.Status);
    }

    [Fact]
    public async Task K1_Decompress_Step2ErrorMarker_MarksFailed()
    {
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("k1_e.dat", 8192);

        var service = new CompressionService(StepRunner(
            (0, "", ""),
            (0, "  [ERR] x", "")));

        var item = new FileItemInfo { FilePath = path, FileName = "k1_e.dat", Size = 8192, AllocatedSize = 4096 };
        bool success = await service.DecompressFileAsync(item);

        Assert.False(success);
    }

    // ------------------------------------------------------------------
    // K2: boş tarama sonucu artık çökmemeli
    // ------------------------------------------------------------------

    [Fact]
    public async Task K2_EmptyScanResult_DoesNotThrow()
    {
        var vm = new MainViewModel(
            new FakeScanner { ResultsToReturn = [] },
            new SelectiveCompressionService([]),
            new FakeAdmin())
        {
            SelectedPath = @"C:\BosKlasor"
        };

        // Düzeltmeden önce: ReorderFilesBySizeDescending ordered[0] ile
        // ArgumentOutOfRangeException fırlatıyordu ve tarama "hata" bitiyordu.
        await vm.ExecuteScanAsync();

        Assert.Empty(vm.Files);
        Assert.False(vm.IsScanning);
        Assert.DoesNotContain("Tarama hatası", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // K3: CaptureSettings dil tercihini korumalı
    // ------------------------------------------------------------------

    [Fact]
    public void K3_CaptureSettings_PersistsLanguageFromLoadedSettings()
    {
        string folder = NewTempFolder();
        try
        {
            var settingsService = new JsonSettingsService(folder);
            settingsService.Save(new AppSettings { Language = "en" });

            var vm = new MainViewModel(
                new FakeScanner(),
                new SelectiveCompressionService([]),
                new FakeAdmin(),
                settingsService: settingsService);

            Assert.Equal("en", vm.CaptureSettings().Language);

            // Simüle edilmiş pencere kapanışı: kayıt dili ezmemeli.
            vm.PersistSettings();
            Assert.Equal("en", new JsonSettingsService(folder).Load().Language);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void K3_CaptureSettings_DefaultsToTurkishWithoutLoadedSettings()
    {
        var vm = new MainViewModel(
            new FakeScanner(),
            new SelectiveCompressionService([]),
            new FakeAdmin());

        Assert.Equal("tr", vm.CaptureSettings().Language);
    }

    // ------------------------------------------------------------------
    // K5: geçmiş — başarısız dosyalar geçmişe yazılmamalı
    // ------------------------------------------------------------------

    private static void AddSelectedFile(MainViewModel vm, string path, long size)
    {
        var model = new FileItemInfo
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            Size = size,
            AllocatedSize = size
        };
        vm.Files.Add(new FileItemViewModel(model) { IsSelected = true });
    }

    [Fact]
    public async Task K5_Compress_FailedFilesAreExcludedFromHistory()
    {
        using var tempDir = new TempTestDirectory();
        string good1 = tempDir.CreateFile("good1.dat", 8192);
        string bad = tempDir.CreateFile("bad.dat", 8192);
        string good2 = tempDir.CreateFile("good2.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            var vm = new MainViewModel(
                new FakeScanner(),
                new SelectiveCompressionService([bad]),
                new FakeAdmin(),
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            AddSelectedFile(vm, good1, 8192);
            AddSelectedFile(vm, bad, 8192);
            AddSelectedFile(vm, good2, 8192);

            await vm.ExecuteCompressSelectedAsync();

            var lastBatch = new JsonCompressionHistoryService(historyFolder).LoadLastBatch();

            Assert.Equal(2, lastBatch.Count);
            Assert.DoesNotContain(lastBatch, e => e.FilePath == bad);
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    [Fact]
    public async Task K5_CompressCancel_PartiallyCompressedFilesAreRecorded()
    {
        using var tempDir = new TempTestDirectory();
        string file1 = tempDir.CreateFile("c1.dat", 8192);
        string file2 = tempDir.CreateFile("c2.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            // İlk dosya sıkışır, ikincisinde OCE fırlatılır (iptal).
            var vm = new MainViewModel(
                new FakeScanner(),
                new SelectiveCompressionService([], cancelAfterFirstSuccess: true),
                new FakeAdmin(),
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            AddSelectedFile(vm, file1, 8192);
            AddSelectedFile(vm, file2, 8192);

            await vm.ExecuteCompressSelectedAsync();

            var lastBatch = new JsonCompressionHistoryService(historyFolder).LoadLastBatch();
            var entry = Assert.Single(lastBatch);
            Assert.Equal(file1, entry.FilePath);
            Assert.Contains(vm.ActivityLogs, l => l.Message.Contains("geri alınabilir kayıt"));
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    [Fact]
    public async Task K5_PartialRevert_PreservesFailedEntriesInHistory()
    {
        using var tempDir = new TempTestDirectory();
        string okPath = tempDir.CreateFile("r_ok.dat", 8192);
        string failPath = tempDir.CreateFile("r_fail.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            var history = new JsonCompressionHistoryService(historyFolder);
            history.AppendBatch(
            [
                new CompressionHistoryEntry { FilePath = okPath, PreviousAlgorithm = "None", NewAlgorithm = "Ntfs" },
                new CompressionHistoryEntry { FilePath = failPath, PreviousAlgorithm = "None", NewAlgorithm = "Ntfs" }
            ]);

            var vm = new MainViewModel(
                new FakeScanner(),
                new SelectiveCompressionService([failPath]),
                new FakeAdmin(),
                historyService: history);

            await vm.ExecuteRestoreLastOperationAsync();

            // Düzeltmeden önce batch koşulsuz siliniyordu; artık yalnız
            // başarısız girdiler yeni batch olarak korunur.
            var remaining = history.Load();
            var remainingEntry = Assert.Single(remaining);
            Assert.Equal(failPath, remainingEntry.FilePath);
            Assert.Contains(vm.ActivityLogs, l => l.Message.Contains("kurtarma kaydı geçmişte korundu"));
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    // ==================================================================
    // İnceleme turu 2 regresyonları (T2-K1 … T2-K4, T2-K6)
    // ==================================================================

    [Theory]
    [InlineData(0, Visibility.Visible)]
    [InlineData(1, Visibility.Collapsed)]
    [InlineData(7, Visibility.Collapsed)]
    public void T2K1_CountToVisibilityConverter_IntCountControlsEmptyPlaceholder(int count, Visibility expected)
    {
        // Tur 2 öncesi: Files.Count (int), BoolToVisibilityConverter'a gidiyordu;
        // converter bool tanımadığından yer tutucu HEP görünür kalıyordu.
        var converter = new CountToVisibilityConverter();

        var result = converter.Convert(count, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void T2K1_CountToVisibilityConverter_NonNumericFallsBackToVisible()
    {
        var converter = new CountToVisibilityConverter();

        Assert.Equal(Visibility.Visible, converter.Convert("metin", typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task T2K2_CompressCancel_PreviouslyCompressedFilesAreNotRecorded()
    {
        // Dosya ÖNCEKİ bir işlemde sıkışmış (Status="Compressed"), yeni batch'e
        // seçilmiş ama batch ona ulaşmadan İPTAL edilmiş. Sahte servis hiçbir
        // dosyaya dokunmuyor → bu dosya için geri alma kaydı ÜRETİLMEMELİ.
        using var tempDir = new TempTestDirectory();
        string alreadyCompressedPath = tempDir.CreateFile("pre.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            var vm = new MainViewModel(
                new FakeScanner(),
                new CancellingCompressionService(),
                new FakeAdmin(),
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            var model = new FileItemInfo
            {
                FilePath = alreadyCompressedPath,
                FileName = Path.GetFileName(alreadyCompressedPath),
                Size = 8192,
                AllocatedSize = 4096,
                Status = "Compressed",
                IsCompressed = true,
                CurrentAlgorithm = CompressionAlgorithm.Ntfs
            };
            vm.Files.Add(new FileItemViewModel(model) { IsSelected = true });

            await vm.ExecuteCompressSelectedAsync();

            var lastBatch = new JsonCompressionHistoryService(historyFolder).LoadLastBatch();

            Assert.Empty(lastBatch);
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    [Fact]
    public async Task T2K3_ScanStreamsFiles_SelectionMetricsStillCorrectAfterScan()
    {
        // T2-K3, akış sırasında madde başına metrik güncellemesini bastırıyor;
        // işlev kaybı olmadığı kilitlenir: uncompressed dosyalar öntanımlı
        // seçili geliyor, compressed değil; sayaçlar tarama sonunda doğru.
        using var tempDir = new TempTestDirectory();
        string uncompressed = tempDir.CreateFile("plain.dat", 8192);
        string compressed = tempDir.CreateFile("packed.dat", 8192);

        var scanner = new FakeScanner
        {
            ResultsToReturn =
            [
                new FileItemInfo
                {
                    FilePath = uncompressed,
                    FileName = "plain.dat",
                    Size = 8192,
                    AllocatedSize = 8192,
                    Status = "Pending",
                    IsCompressed = false
                },
                new FileItemInfo
                {
                    FilePath = compressed,
                    FileName = "packed.dat",
                    Size = 8192,
                    AllocatedSize = 4096,
                    Status = "Compressed",
                    IsCompressed = true,
                    CurrentAlgorithm = CompressionAlgorithm.Ntfs
                }
            ]
        };

        var vm = new MainViewModel(scanner, new SelectiveCompressionService([]), new FakeAdmin())
        {
            SelectedPath = tempDir.Path
        };

        await vm.ExecuteScanAsync();

        Assert.Equal(2, vm.Files.Count);
        Assert.Equal(1, vm.SelectedFilesCount);
        Assert.True(vm.Files.Single(f => Path.GetFileName(f.FilePath) == "plain.dat").IsSelected);
        Assert.False(vm.Files.Single(f => Path.GetFileName(f.FilePath) == "packed.dat").IsSelected);
    }

    [Fact]
    public void T2K3_SelectAllAndUpdateMetrics_OncePerBulkOperation()
    {
        // Toplu seçim yolları madde başına tam liste taraması yapmamalı;
        // sonucun doğru kaldığı (tek güncelleme) işlevsel kilidi.
        var vm = new MainViewModel(
            new FakeScanner(),
            new SelectiveCompressionService([]),
            new FakeAdmin());

        for (int i = 1; i <= 50; i++)
        {
            AddSelectedFile(vm, $@"C:\bulk\f{i}.dat", 1024 * i);
        }

        vm.SelectAllCommand.Execute(null);
        Assert.Equal(50, vm.SelectedFilesCount);

        vm.ExecuteInvertSelection();
        Assert.Equal(0, vm.SelectedFilesCount);
    }

    [Fact]
    public async Task T2K4_Revert_UnknownPreviousAlgorithmIsTreatedAsDecompressedOnly()
    {
        // "Unknown" önceki algoritma yeniden uygulanamaz: CompressionService
        // ArgumentOutOfRangeException fırlatır ve dosya sonsuza dek "Failed"
        // olurdu. Şimdi açılmış hâl başarılı sayılır, CompressFileAsync çağrılmaz.
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("unk.dat", 8192);

        var compression = new SelectiveCompressionService([]);
        var revertService = new CompressionRevertService(compression);

        var batch = new List<CompressionHistoryEntry>
        {
            new() { FilePath = path, PreviousAlgorithm = "Unknown", NewAlgorithm = "Lzx" }
        };

        var summary = await revertService.RevertBatchAsync(batch, progress: null, CancellationToken.None);

        Assert.Equal(0, summary.FailedCount);
        Assert.Equal(1, summary.SuccessfulCount);
        Assert.Equal(1, compression.DecompressCalls);
        Assert.Equal(0, compression.CompressCalls);
    }

    [Fact]
    public void T2K6_SettingsSave_FailureLeavesNoTempFile()
    {
        // settings.json'i başkası kilitli tutarken Save, File.Replace'te
        // IOException ile düşer; düzeltme olmadan .tmp kalıntısı kalıyordu.
        string folder = NewTempFolder();
        try
        {
            var service = new JsonSettingsService(folder);
            File.WriteAllText(service.SettingsPath, "{}");

            using (new FileStream(service.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<Exception>(() => service.Save(new AppSettings()));
            }

            Assert.False(File.Exists(service.SettingsPath + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    // ==================================================================
    // İnceleme turu 3 regresyonları (T3-B1, T3-N1, T3-N2, T3-N3)
    // ==================================================================

    [Fact]
    public void T3B1_ProductionDependencies_ShareSingleSettingsServiceInstance()
    {
        // Tur 3 öncesi: MainWindow üretim ctor'u VM'e ayrı bir JsonSettingsService
        // veriyor, pencere tarafı null kalıyordu — kapanışta hiçbir ayar
        // kaydedilmiyor, pencere konumu yüklenmiyordu. Artık TEK örnek paylaşılır.
        var (viewModel, settings) = MainWindow.CreateProductionDependencies();

        Assert.IsType<JsonSettingsService>(settings);
        Assert.Same(settings, viewModel.SettingsService);
    }

    [Fact]
    public async Task T3N1_CompressCancel_AlgorithmChangedPreviouslyCompressedFileIsRecorded()
    {
        // Önceden Xpress4K ile sıkışmış dosya, yeni batch'te Ntfs'e GEÇMİŞ ve
        // batch daha sonra iptal edilmiş. Bu gerçek bir algoritma değişimidir —
        // kurtarma kaydı YAZILMALI (T3-N1 öncesi under-recording vardı).
        using var tempDir = new TempTestDirectory();
        string changedPath = tempDir.CreateFile("changed.dat", 8192);
        string untouchedPath = tempDir.CreateFile("untouched.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            var vm = new MainViewModel(
                new FakeScanner(),
                new SelectiveCompressionService([], cancelAfterFirstSuccess: true),
                new FakeAdmin(),
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            var changedModel = new FileItemInfo
            {
                FilePath = changedPath,
                FileName = "changed.dat",
                Size = 8192,
                AllocatedSize = 4096,
                Status = "Compressed",
                IsCompressed = true,
                CurrentAlgorithm = CompressionAlgorithm.Xpress4K
            };
            var untouchedModel = new FileItemInfo
            {
                FilePath = untouchedPath,
                FileName = "untouched.dat",
                Size = 8192,
                AllocatedSize = 8192,
                Status = "Pending",
                CurrentAlgorithm = CompressionAlgorithm.None
            };
            vm.Files.Add(new FileItemViewModel(changedModel) { IsSelected = true });
            vm.Files.Add(new FileItemViewModel(untouchedModel) { IsSelected = true });

            await vm.ExecuteCompressSelectedAsync();

            var lastBatch = new JsonCompressionHistoryService(historyFolder).LoadLastBatch();
            var entry = Assert.Single(lastBatch);
            Assert.Equal(changedPath, entry.FilePath);
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    [Theory]
    [InlineData("satır1\nsatır2", "satır1 satır2")]
    [InlineData("hata\r\ndetay", "hata detay")]
    [InlineData("x;y", "\"x;y\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("düz metin", "düz metin")]
    public void T3N2_EscapeCsv_NewlinesDoNotBreakCsvRows(string input, string expected)
    {
        // Çok satırlı mesajlar (özellikle exception logları) dışa aktarılan CSV'nin
        // satır yapısını bozuyordu; satır sonları artık boşluğa indirgenir.
        Assert.Equal(expected, MainViewModel.EscapeCsv(input));
    }

    [Fact]
    public void T3N3_OpenFileLocation_MissingFileLogsWarningInsteadOfSilentNoOp()
    {
        var vm = new MainViewModel(
            new FakeScanner(),
            new SelectiveCompressionService([]),
            new FakeAdmin());

        var item = new FileItemViewModel(new FileItemInfo
        {
            FilePath = @"C:\wc_yok\byle_bir_dosya.dat",
            FileName = "byle_bir_dosya.dat",
            Size = 100
        });
        vm.Files.Add(item);

        vm.OpenFileLocationCommand.Execute(item);

        Assert.Contains(vm.ActivityLogs, l => l.Message.Contains("Dosya bulunamadı"));
    }

    // ==================================================================
    // İnceleme turu 4 regresyonları (T4-M1, T4-N2)
    // ==================================================================

    [Fact]
    public async Task T4M1_DecompressBatch_RemovesDecompressedFilesFromHistory()
    {
        // Tur 4 öncesi: toplu "Sıkıştırmayı Kaldır" geçmişi değiştirmiyordu;
        // "Son İşlemi Geri Al" bayat batch'i uygulayıp az önce AÇILAN dosyaları
        // fark ettirmeden yeniden sıkıştırıyordu.
        using var tempDir = new TempTestDirectory();
        string fileA = tempDir.CreateFile("m1_a.dat", 8192);
        string fileB = tempDir.CreateFile("m1_b.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            var compression = new SelectiveCompressionService([]);
            var vm = new MainViewModel(
                new FakeScanner(),
                compression,
                new FakeAdmin(),
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            AddSelectedFile(vm, fileA, 8192);
            AddSelectedFile(vm, fileB, 8192);

            await vm.ExecuteCompressSelectedAsync();
            var history = new JsonCompressionHistoryService(historyFolder);
            Assert.Equal(2, history.LoadLastBatch().Count);

            await vm.ExecuteDecompressSelectedAsync();

            // Açılan dosyaların geri alma kaydı düşürülmeli; son batch
            // boşaldığından "Son İşlemi Geri Al" artık uygulanamaz.
            Assert.Empty(history.Load());
            Assert.False(vm.CanRestoreLastOperation);
            Assert.Contains(vm.ActivityLogs, l => l.Message.Contains("geri alma kaydı geçmişten düşürüldü"));
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    [Fact]
    public async Task T4M1_DecompressBatch_KeepsEntriesOfFilesThatFailedToDecompress()
    {
        // Kısmi decompress'te yalnız BAŞARILI açılanlar düşürülmeli; açılamayan
        // dosyanın kurtarma kaydı (eski algoritmaya dönüş) geçmişte kalmalı.
        using var tempDir = new TempTestDirectory();
        string okPath = tempDir.CreateFile("m1_ok.dat", 8192);
        string failPath = tempDir.CreateFile("m1_fail.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            // Compress her iki dosyada başarılı; Decompress yalnız failPath'te başarısız.
            var compression = new SelectiveCompressionService([]);
            var vm = new MainViewModel(
                new FakeScanner(),
                compression,
                new FakeAdmin(),
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            AddSelectedFile(vm, okPath, 8192);
            AddSelectedFile(vm, failPath, 8192);
            await vm.ExecuteCompressSelectedAsync();

            compression.FailOnDecompress(failPath);
            await vm.ExecuteDecompressSelectedAsync();

            var remaining = new JsonCompressionHistoryService(historyFolder).Load();
            var entry = Assert.Single(remaining);
            Assert.Equal(failPath, entry.FilePath);
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    [Fact]
    public async Task T4M1_DecompressSingleFile_RemovesItsHistoryEntry()
    {
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("m1_single.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            var vm = new MainViewModel(
                new FakeScanner(),
                new SelectiveCompressionService([]),
                new FakeAdmin(),
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            AddSelectedFile(vm, path, 8192);
            await vm.ExecuteCompressSelectedAsync();

            var item = vm.Files.Single(f => f.FilePath == path);
            await vm.ExecuteDecompressSingleFileAsync(item);

            Assert.Empty(new JsonCompressionHistoryService(historyFolder).Load());
            Assert.False(vm.CanRestoreLastOperation);
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    [Fact]
    public async Task T4N2_CliCompress_AdminAlgorithmAsNonAdmin_FailsFastWithClearError()
    {
        // Tur 4 öncesi: non-admin + lzx → her dosya tek tek compact.exe hatasıyla
        // düşüyordu (gürültülü, kafa karıştırıcı). Artık tek açık hata + exit 1.
        using var tempDir = new TempTestDirectory();

        var output = new System.IO.StringWriter();
        var error = new System.IO.StringWriter();
        var cli = new CliDispatcher(
            new FakeScanner(),
            new SelectiveCompressionService([]),
            estimator: null,
            output: output,
            error: error,
            adminService: new FakeAdmin());

        int exitCode = await cli.RunAsync(["compress", tempDir.Path, "--algorithm", "lzx"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("yönetici", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ==================================================================
    // İnceleme turu 5 regresyonları (T5-M1, T5-M2, T5-N4)
    // ==================================================================

    [Fact]
    public async Task T5M1_CompressSingleFile_CancelReportsCanceledInsteadOfError()
    {
        // Tur 5 öncesi: tekil işlemlerde İptal → OCE genel catch'e düşüyor,
        // kullanıcı "Hata: The operation was canceled." görüyordu.
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("m1_iptal_compress.dat", 8192);

        var compression = new SelectiveCompressionService([]);
        compression.CancelOnCompress(path);

        var vm = new MainViewModel(
            new FakeScanner(),
            compression,
            new FakeAdmin())
        {
            SelectedAlgorithm = CompressionAlgorithm.Ntfs
        };

        AddSelectedFile(vm, path, 8192);
        var item = vm.Files.Single(f => f.FilePath == path);

        await vm.ExecuteCompressSingleFileAsync(item);

        Assert.Contains("iptal edildi", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hata", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
        Assert.Contains(vm.ActivityLogs, l => l.Message.Contains("iptal edildi"));
    }

    [Fact]
    public async Task T5M1_DecompressSingleFile_CancelReportsCanceledInsteadOfError()
    {
        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("m1_iptal_decompress.dat", 8192);

        var compression = new SelectiveCompressionService([]);
        compression.CancelOnDecompress(path);

        var vm = new MainViewModel(
            new FakeScanner(),
            compression,
            new FakeAdmin());

        AddSelectedFile(vm, path, 8192);
        var item = vm.Files.Single(f => f.FilePath == path);

        await vm.ExecuteDecompressSingleFileAsync(item);

        Assert.Contains("iptal edildi", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hata", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task T5M2_DecompressBatch_CancelDropsAlreadyDecompressedFilesFromHistory()
    {
        // Tur 5 öncesi: toplu decompress İPTAL yolunda bayat girdiler kalıyordu;
        // "Son İşlemi Geri Al" iptal anına kadar açılan dosyaları yeniden sıkıştırıyordu.
        using var tempDir = new TempTestDirectory();
        string fileA = tempDir.CreateFile("m2_a.dat", 8192);
        string fileB = tempDir.CreateFile("m2_b.dat", 8192);

        string historyFolder = NewTempFolder();
        try
        {
            var compression = new SelectiveCompressionService([]);
            var vm = new MainViewModel(
                new FakeScanner(),
                compression,
                new FakeAdmin(),
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            AddSelectedFile(vm, fileA, 8192);
            AddSelectedFile(vm, fileB, 8192);
            await vm.ExecuteCompressSelectedAsync();
            Assert.Equal(2, new JsonCompressionHistoryService(historyFolder).LoadLastBatch().Count);

            // İlk dosya açıldıktan hemen sonra kullanıcı İptal'e basıyor.
            compression.CancelDecompressAfter(1);
            await vm.ExecuteDecompressSelectedAsync();

            // Açılan (A) dosyanın girdisi düşürülmeli; açılamayan (B) kalmalı.
            var remaining = new JsonCompressionHistoryService(historyFolder).Load();
            var entry = Assert.Single(remaining);
            Assert.Equal(fileB, entry.FilePath);

            Assert.Contains("iptal edildi", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(vm.ActivityLogs, l => l.Message.Contains("geri alma kaydı geçmişten düşürüldü"));
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    [Fact]
    public async Task T5N4_AsyncRelayCommand_SwallowsExceptionAndResetsIsExecuting()
    {
        // T4-N5/T5-N4: komut gövdesi kendi try'ına girmeden fırlayan istisna
        // async void'dan süreci düşürmemeli (genel catch sigortası) ve
        // IsExecuting resetlenmeli.
        var command = new WinCompressor.UI.ViewModels.AsyncRelayCommand(
            () => Task.FromException(new InvalidOperationException("simüle patlama")));

        command.Execute(null); // fırlamamalı

        await Task.Delay(100); // async void gövdesinin tamamlanmasına izin ver

        Assert.False(command.IsExecuting);
    }
}
