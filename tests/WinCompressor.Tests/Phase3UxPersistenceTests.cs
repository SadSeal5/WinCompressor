using System.Text;
using System.Text.Json;
using WinCompressor.Core.Models;
using WinCompressor.Core.Native;
using WinCompressor.Core.Services;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

/// <summary>
/// Faz 3: ayar kalıcılığı (JSON servis + VM uygulanması), streaming tarama,
/// AppData dahil etme seçeneği, uzun yol (8.3 kısa yol fallback) desteği,
/// günlük seviyeleri/dışa aktarma ve dosya açma komutu.
/// </summary>
public class Phase3UxPersistenceTests
{
    private class FakeAdmin : IAdminService
    {
        public bool AdminStatus { get; set; }
        public bool IsAdministrator() => AdminStatus;
        public bool RestartAsAdmin(string? arguments = null) => true;
    }

    private class NoopCompression : ICompressionService
    {
        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<CompressionResultSummary> CompressFilesAsync(
            IEnumerable<FileItemInfo> files,
            CompressionAlgorithm algorithm,
            int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompressionResultSummary { TotalFiles = 0, SuccessfulCount = 0, FailedCount = 0 });

        public Task<CompressionResultSummary> DecompressFilesAsync(
            IEnumerable<FileItemInfo> files,
            int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompressionResultSummary { TotalFiles = 0, SuccessfulCount = 0, FailedCount = 0 });
    }

    private class CapturingScanner : IFileScannerService
    {
        public List<FileItemInfo> ResultsToReturn { get; set; } = [];

        public Task<IReadOnlyList<FileItemInfo>> ScanAsync(
            ScanOptions options,
            IProgress<ScanProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Simulates a streaming scanner: emits every file in a separate
            // batch, then also returns the full list (like FileScannerService).
            foreach (var item in ResultsToReturn)
            {
                progress?.Report(new ScanProgressReport
                {
                    MatchedFilesCount = 1,
                    MatchedFilesBatch = [item]
                });
            }

            return Task.FromResult<IReadOnlyList<FileItemInfo>>(ResultsToReturn);
        }

        public bool IsRootDrive(string path) => false;
    }

    private class FakeSettingsService : ISettingsService
    {
        public AppSettings ToLoad { get; set; } = new();
        public AppSettings? Stored { get; private set; }
        public int SaveCalls { get; private set; }

        public AppSettings Load() => ToLoad;

        public void Save(AppSettings settings)
        {
            Stored = settings;
            SaveCalls++;
        }
    }

    private static FileItemInfo MakeItem(string name, long size, bool isCompressed = false) => new()
    {
        FileName = name,
        FilePath = @"C:\" + name,
        Extension = System.IO.Path.GetExtension(name),
        Size = size,
        AllocatedSize = isCompressed ? size / 2 : size,
        IsCompressed = isCompressed,
        CurrentAlgorithm = isCompressed ? CompressionAlgorithm.Ntfs : CompressionAlgorithm.None
    };

    // ------------------------------------------------------------------
    // 3.1 Ayar servisi (JSON)
    // ------------------------------------------------------------------

    [Fact]
    public void JsonSettings_SaveLoadRoundtrip()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc_settings_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new JsonSettingsService(folder);
            var settings = new AppSettings
            {
                LastPath = @"D:\Games",
                LastAlgorithm = "Ntfs",
                LastCategory = "DataMedia",
                IncludeSubdirectories = false,
                OnlyUncompressed = false,
                IncludeAppData = true,
                ActivityLogVisible = true,
                WindowLeft = 10,
                WindowTop = 20,
                WindowWidth = 800,
                WindowHeight = 600,
                WindowMaximized = true
            };

            service.Save(settings);
            Assert.True(File.Exists(service.SettingsPath));

            var loaded = new JsonSettingsService(folder).Load();

            Assert.Equal(settings.LastPath, loaded.LastPath);
            Assert.Equal(settings.LastAlgorithm, loaded.LastAlgorithm);
            Assert.Equal(settings.LastCategory, loaded.LastCategory);
            Assert.False(loaded.IncludeSubdirectories);
            Assert.False(loaded.OnlyUncompressed);
            Assert.True(loaded.IncludeAppData);
            Assert.True(loaded.ActivityLogVisible);
            Assert.Equal(10, loaded.WindowLeft);
            Assert.Equal(20, loaded.WindowTop);
            Assert.Equal(800, loaded.WindowWidth);
            Assert.Equal(600, loaded.WindowHeight);
            Assert.True(loaded.WindowMaximized);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void JsonSettings_MissingFile_ReturnsDefaults()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc_settings_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var loaded = new JsonSettingsService(folder).Load();

            Assert.Equal(new AppSettings().LastPath, loaded.LastPath);
            Assert.True(loaded.IncludeSubdirectories);
            Assert.False(loaded.IncludeAppData);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void JsonSettings_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc_settings_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(System.IO.Path.Combine(folder, "settings.json"), "{ this is : not json !");

            var loaded = new JsonSettingsService(folder).Load();

            Assert.Equal(new AppSettings().LastPath, loaded.LastPath);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // 3.1 VM: ayarların uygulanması ve yakalanması
    // ------------------------------------------------------------------

    [Fact]
    public void MainViewModel_LoadsPersistedPreferencesAtStartup()
    {
        using var tempDir = new TempTestDirectory();
        var settings = new FakeSettingsService
        {
            ToLoad = new AppSettings
            {
                LastPath = tempDir.Path,
                LastAlgorithm = "Ntfs",
                LastCategory = "DataMedia",
                IncludeSubdirectories = false,
                OnlyUncompressed = false,
                IncludeAppData = true,
                ActivityLogVisible = true
            }
        };

        var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin(), null, settings);

        Assert.Equal(tempDir.Path, vm.SelectedPath);
        Assert.Equal(CompressionAlgorithm.Ntfs, vm.SelectedAlgorithm);
        Assert.Equal(FileCategory.DataMedia, vm.SelectedCategory);
        Assert.False(vm.IncludeSubdirectories);
        Assert.False(vm.OnlyUncompressed);
        Assert.True(vm.IncludeAppData);
        Assert.True(vm.IsActivityLogVisible);
    }

    [Fact]
    public void MainViewModel_DoesNotRestoreAdminAlgorithmForStandardUser()
    {
        var settings = new FakeSettingsService
        {
            ToLoad = new AppSettings { LastAlgorithm = "Lzx" }
        };

        var nonAdmin = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin { AdminStatus = false }, null, settings);
        Assert.Equal(CompressionAlgorithm.Ntfs, nonAdmin.SelectedAlgorithm);

        // Admin runs DO restore the persisted admin-only algorithm.
        var adminSettings = new FakeSettingsService
        {
            ToLoad = new AppSettings { LastAlgorithm = "Lzx" }
        };
        var admin = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin { AdminStatus = true }, null, adminSettings);
        Assert.Equal(CompressionAlgorithm.Lzx, admin.SelectedAlgorithm);
    }

    [Fact]
    public void MainViewModel_IgnoresInvalidPersistedValues()
    {
        var settings = new FakeSettingsService
        {
            ToLoad = new AppSettings
            {
                LastPath = @"Q:\Böyle Bir Klasör Yok\",
                LastAlgorithm = ((CompressionAlgorithm)77).ToString(),
                LastCategory = "BüyükDosyalarYokBöyleBirKategori"
            }
        };

        var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin { AdminStatus = false }, null, settings);

        // Path/geçersiz değerler durağan başlangıç durumunu bozmaz.
        Assert.True(Directory.Exists(vm.SelectedPath));
        Assert.Equal(CompressionAlgorithm.Ntfs, vm.SelectedAlgorithm);
        Assert.Equal(FileCategory.All, vm.SelectedCategory);
    }

    [Fact]
    public void CapturedSettings_ReflectCurrentState_AndPersistThroughService()
    {
        var settings = new FakeSettingsService();
        var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin { AdminStatus = true }, null, settings)
        {
            SelectedAlgorithm = CompressionAlgorithm.Xpress16K,
            IncludeSubdirectories = false,
            IncludeAppData = true,
            IsActivityLogVisible = true
        };
        vm.SetCategory(FileCategory.CompactOs);

        vm.PersistSettings();

        Assert.Equal(1, settings.SaveCalls);
        Assert.NotNull(settings.Stored);
        Assert.Equal(CompressionAlgorithm.Xpress16K.ToString(), settings.Stored!.LastAlgorithm);
        Assert.Equal(FileCategory.CompactOs.ToString(), settings.Stored.LastCategory);
        Assert.False(settings.Stored.IncludeSubdirectories);
        Assert.True(settings.Stored.IncludeAppData);
        Assert.True(settings.Stored.ActivityLogVisible);
    }

    [Fact]
    public void PersistSettings_WithoutService_IsNoop()
    {
        var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin());
        vm.PersistSettings(); // exception yok
        Assert.True(true);
    }

    // ------------------------------------------------------------------
    // 3.4 Streaming tarama
    // ------------------------------------------------------------------

    [Fact]
    public async Task Scanner_ReportsMatchedFilesInBatchesWhileScanning()
    {
        using var tempDir = new TempTestDirectory();
        tempDir.CreateFile("01.bin", 8192);
        tempDir.CreateFile("02.bin", 8192);
        tempDir.CreateFile("03.bin", 8192);
        tempDir.CreateFile("04.bin", 8192);
        tempDir.CreateFile("05.bin", 8192);

        var scanner = new FileScannerService();
        var reports = new List<ScanProgressReport>();

        var results = await scanner.ScanAsync(new ScanOptions
        {
            RootPath = tempDir.Path,
            IncludeSubdirectories = false,
            FileReportBatchSize = 2
        }, new SyncProgress<ScanProgressReport>(reports.Add));

        Assert.Equal(5, results.Count);

        var batches = reports.Where(r => r.MatchedFilesBatch is { Count: > 0 }).ToList();
        Assert.True(batches.Count >= 2, $"En az 2 chunk beklenirdi, {batches.Count} geldi.");
        Assert.All(batches, b => Assert.InRange(b.MatchedFilesBatch!.Count, 1, 2));

        var streamedPaths = batches
            .SelectMany(b => b.MatchedFilesBatch!)
            .Select(f => f.FilePath)
            .Distinct()
            .Count();
        Assert.Equal(5, streamedPaths);
    }

    [Fact]
    public async Task ViewModel_StreamingBatchesDoNotDuplicateFinalResults()
    {
        var scanner = new CapturingScanner
        {
            ResultsToReturn =
            [
                MakeItem("a.bin", 1000),
                MakeItem("b.bin", 5000),
                MakeItem("c.bin", 3000)
            ]
        };
        var vm = new MainViewModel(scanner, new NoopCompression(), new FakeAdmin()) { SelectedPath = @"C:\test" };

        await vm.ExecuteScanAsync();

        Assert.Equal(3, vm.Files.Count);
        Assert.Equal(3, vm.TotalFilesFound);
        Assert.Equal(9000, vm.TotalBytesFound);
        // INVARIANT: tarama sonrası Files boyuta göre azalan sırada.
        Assert.Equal(5000, vm.Files[0].Size);
        Assert.Equal(3000, vm.Files[1].Size);
        Assert.Equal(1000, vm.Files[2].Size);
    }

    // ------------------------------------------------------------------
    // 3.6 "AppData'yı dahil et" seçeneği
    // ------------------------------------------------------------------

    [Fact]
    public async Task Scanner_AppDataExcludedByDefault_IncludedOnOptIn()
    {
        using var tempDir = new TempTestDirectory();
        tempDir.CreateFile("root.bin", 8192);
        tempDir.CreateFile(System.IO.Path.Combine("AppData", "inside.bin"), 8192);
        tempDir.CreateFile(System.IO.Path.Combine("AppData", "Roaming", "deep.bin"), 8192);

        var scanner = new FileScannerService();

        var defaultScan = await scanner.ScanAsync(new ScanOptions
        {
            RootPath = tempDir.Path,
            IncludeSubdirectories = true
        });
        Assert.Single(defaultScan);
        Assert.Equal("root.bin", defaultScan[0].FileName);

        var optInScan = await scanner.ScanAsync(new ScanOptions
        {
            RootPath = tempDir.Path,
            IncludeSubdirectories = true,
            IncludeAppData = true
        });
        Assert.Equal(3, optInScan.Count);
    }

    // ------------------------------------------------------------------
    // 3.7 Uzun yol: compact.exe kısa yol (8.3) fallback'i
    // ------------------------------------------------------------------

    [Fact]
    public async Task Compression_LongPath_FallsBackToShortPathAndSucceeds()
    {
        using var tempDir = new TempTestDirectory();

        // MAX_PATH aşan iç içe klasör zinciri kur.
        var dir = tempDir.Path;
        var longPath = System.IO.Path.Combine(dir, "longfile.bin");
        while (longPath.Length < 270)
        {
            dir = System.IO.Path.Combine(dir, "level_" + new string('x', 25));
            longPath = System.IO.Path.Combine(dir, "longfile.bin");
        }
        Directory.CreateDirectory(dir);
        var buffer = new byte[64 * 1024];
        Array.Fill(buffer, (byte)'Z');
        await File.WriteAllBytesAsync(longPath, buffer);

        Assert.True(longPath.Length >= 260, $"Test yolu minimum 260 karakter olmalı, {longPath.Length} geldi.");

        // 8.3 kısa adları kapalı birimde test atlanır (fallback mümkün değil).
        var shortPath = Kernel32.TryGetShortPath(longPath);
        if (shortPath is null || shortPath.Length >= 260)
            return;

        var item = new FileItemInfo
        {
            FilePath = longPath,
            FileName = "longfile.bin",
            Extension = ".bin",
            Size = 64 * 1024,
            AllocatedSize = 64 * 1024
        };

        var service = new CompressionService();
        bool success = await service.CompressFileAsync(item, CompressionAlgorithm.Ntfs);

        Assert.True(success, $"Uzun yol sıkıştırma başarısız: {item.ErrorMessage}");
        Assert.True(item.IsCompressed);
        Assert.True(item.AllocatedSize < item.Size, "Sıkıştırılmış dosyanın tahsis boyutu küçülmüş olmalı.");
    }

    /// <summary>Deterministik IProgress: Report doğrudan çağrılır (senkron).</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;
        public SyncProgress(Action<T> onReport) => _onReport = onReport;
        public void Report(T value) => _onReport(value);
    }

    // ------------------------------------------------------------------
    // 3.5 Günlük: seviyeler ve dışa aktarma
    // ------------------------------------------------------------------

    [Fact]
    public void ActivityLog_Levels_ProduceMarkersAndDisplayText()
    {
        var info = new ActivityLogEntry("bilgi mesajı");
        var warning = new ActivityLogEntry("uyarı mesajı", ActivityLogLevel.Warning);
        var error = new ActivityLogEntry("hata mesajı", ActivityLogLevel.Error);

        Assert.Equal(ActivityLogLevel.Info, info.Level);
        Assert.DoesNotContain("⚠", info.DisplayText);
        Assert.Contains("⚠ ", warning.DisplayText);
        Assert.Contains("✖ ", error.DisplayText);
        Assert.All(new[] { info, warning, error }, e => Assert.StartsWith("[", e.DisplayText));
    }

    [Fact]
    public void ExportActivityLogs_WritesTxtFileWithDisplayLines()
    {
        string target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc_log_test_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin());
            vm.ClearActivityLogsCommand.Execute(null); // başlangıç kaydını at
            vm.LogActivity("bilgi satırı");
            vm.LogWarning("uyarı satırı");
            vm.LogError("hata satırı");
            vm.ExportLogsPathPicker = () => target;

            vm.ExportActivityLogsCommand.Execute(null);

            Assert.True(File.Exists(target));
            var lines = File.ReadAllLines(target);
            Assert.Equal(3, lines.Length);
            Assert.Contains("bilgi satırı", lines[0]);
            Assert.Contains("⚠", lines[1]);
            Assert.Contains("✖", lines[2]);
            Assert.Contains("dışa aktarıldı", vm.ActivityLogs.Last().Message);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public void ExportActivityLogs_WritesCsvWithHeaderAndEscaping()
    {
        string target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc_log_test_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin());
            vm.ClearActivityLogsCommand.Execute(null); // başlangıç kaydını at
            vm.LogActivity("düz mesaj");
            vm.LogError("noktalı;virgüllü \"mesaj\"");
            vm.ExportLogsPathPicker = () => target;

            vm.ExportActivityLogsCommand.Execute(null);

            var lines = File.ReadAllLines(target);
            Assert.Equal("Zaman;Seviye;Mesaj", lines[0]);
            Assert.Contains("düz mesaj", lines[1]);
            // ; ve \" içeren mesajlar CSV tırnak kurallarıyla kaçışlanmalı.
            Assert.Contains("\"noktalı;virgüllü \"\"mesaj\"\"\"", lines[2]);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public void ExportActivityLogs_EmptyLog_CommandCannotExecute()
    {
        var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin());
        vm.ClearActivityLogsCommand.Execute(null);

        Assert.False(vm.ExportActivityLogsCommand.CanExecute(null));
    }

    [Fact]
    public void PickerReturnsNull_ExportDoesNothing()
    {
        var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin());
        vm.LogActivity("kaydedilemeyecek");
        vm.ExportLogsPathPicker = () => null;

        vm.ExportActivityLogsCommand.Execute(null);

        // İptal: durum mesajı değişmez, yeni dosya yok.
        Assert.Equal("Hazır", vm.StatusMessage);
    }

    // ------------------------------------------------------------------
    // 3.3 Dosyayı açma komutu (çift tık / Enter)
    // ------------------------------------------------------------------

    [Fact]
    public void OpenFileCommand_CallsHandlerAndLogs()
    {
        using var tempDir = new TempTestDirectory();
        var path = tempDir.CreateFile("to_open.txt", 4096);

        var vm = new MainViewModel(new CapturingScanner(), new NoopCompression(), new FakeAdmin());
        string? opened = null;
        vm.OpenFileHandler = p => opened = p;

        var item = vm.CreateFileItem(new FileItemInfo
        {
            FilePath = path,
            FileName = "to_open.txt",
            Extension = ".txt",
            Size = 4096,
            AllocatedSize = 4096
        });
        vm.Files.Add(item);
        vm.OpenFileCommand.Execute(vm.Files[0]);

        Assert.Equal(path, opened);
        Assert.Contains(vm.ActivityLogs, e => e.Message.Contains("to_open.txt"));

        // Iptal yolu: var olmayan dosya.
        var missing = vm.CreateFileItem(new FileItemInfo
        {
            FilePath = @"C:\yok_boyle_bir_dosya_12345.dat",
            FileName = "yok_boyle_bir_dosya_12345.dat",
            Size = 100,
            AllocatedSize = 100
        });
        vm.OpenFileCommand.Execute(missing);
        Assert.Contains("bulunamadı", vm.StatusMessage);
        Assert.Equal(ActivityLogLevel.Warning, vm.ActivityLogs.Last().Level);
    }
}
