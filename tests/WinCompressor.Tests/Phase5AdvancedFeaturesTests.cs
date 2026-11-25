using System.IO;
using System.Text.Json;
using WinCompressor.Core.Cli;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

/// <summary>
/// Faz 5: CLI (5.2), geri alma güvenlik ağı (5.4), oyun kütüphaneleri (5.5),
/// zamanlanmış görevler (5.1) ve dil ayarı kalıcılığı (5.3).
/// </summary>
public class Phase5AdvancedFeaturesTests
{
    private class FakeAdmin : IAdminService
    {
        public bool AdminStatus { get; set; }
        public bool IsAdministrator() => AdminStatus;
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

    private class RecordingCompression : ICompressionService
    {
        public List<(FileItemInfo Item, CompressionAlgorithm Algorithm)> Calls { get; } = [];

        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default)
        {
            Calls.Add((fileItem, algorithm));
            fileItem.IsCompressed = true;
            fileItem.CurrentAlgorithm = algorithm;
            fileItem.AllocatedSize = fileItem.Size / 2;
            fileItem.Status = "Compressed";
            return Task.FromResult(true);
        }

        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default)
        {
            Calls.Add((fileItem, CompressionAlgorithm.None));
            fileItem.IsCompressed = false;
            fileItem.CurrentAlgorithm = CompressionAlgorithm.None;
            fileItem.AllocatedSize = fileItem.Size;
            fileItem.Status = "Decompressed";
            return Task.FromResult(true);
        }

        public async Task<CompressionResultSummary> CompressFilesAsync(
            IEnumerable<FileItemInfo> files, CompressionAlgorithm algorithm, int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null, CancellationToken cancellationToken = default)
        {
            int success = 0, failed = 0;
            var items = files.ToList();

            foreach (var file in items)
            {
                if (await CompressFileAsync(file, algorithm, cancellationToken))
                    success++;
                else
                    failed++;
            }

            return BuildSummary(items, success, failed);
        }

        public async Task<CompressionResultSummary> DecompressFilesAsync(
            IEnumerable<FileItemInfo> files, int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null, CancellationToken cancellationToken = default)
        {
            int success = 0, failed = 0;
            var items = files.ToList();

            foreach (var file in items)
            {
                if (await DecompressFileAsync(file, cancellationToken))
                    success++;
                else
                    failed++;
            }

            return BuildSummary(items, success, failed);
        }

        private static CompressionResultSummary BuildSummary(List<FileItemInfo> items, int success, int failed) => new()
        {
            TotalFiles = items.Count,
            SuccessfulCount = success,
            FailedCount = failed,
            TotalOriginalBytes = items.Sum(i => i.Size),
            TotalAllocatedBytes = items.Sum(i => i.AllocatedSize),
            Items = items
        };
    }

    private class FakeGameLibraryService : IGameLibraryService
    {
        public List<GameLibraryInfo> Libraries { get; set; } = [];
        public IReadOnlyList<GameLibraryInfo> DetectInstalledLibraries() => Libraries;
    }

    private class FakeRegistryReader : IRegistryReader
    {
        public Dictionary<string, Dictionary<string, string>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? LocalMachine(string subKey, string valueName) =>
            Keys.TryGetValue(subKey, out var values) && values.TryGetValue(valueName, out var value)
                ? value
                : null;
    }

    private class FakeScheduledTaskService : IScheduledTaskService
    {
        public ScheduledTaskDefinition? Captured { get; private set; }
        public Func<ScheduledTaskDefinition, bool>? Result { get; set; }
        public string? Error { get; set; }

        public string BuildArguments(ScheduledTaskDefinition definition) => "/Create ...";

        public bool TryCreateWeeklyTask(ScheduledTaskDefinition definition, out string? error)
        {
            Captured = definition;
            error = Error;
            return Result?.Invoke(definition) ?? true;
        }
    }

    private static FileItemInfo MakeItem(string name, long size, string path = @"C:\") => new()
    {
        FileName = name,
        FilePath = System.IO.Path.Combine(path, name),
        Extension = System.IO.Path.GetExtension(name),
        Size = size,
        AllocatedSize = size
    };

    // ------------------------------------------------------------------
    // 5.4 Geçmiş servisi
    // ------------------------------------------------------------------

    private static string NewHistoryFolder() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc_history_test_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void HistoryService_AppendLoadLastBatchAndRemove()
    {
        string folder = NewHistoryFolder();
        try
        {
            var history = new JsonCompressionHistoryService(folder, capacity: 4);

            Assert.Empty(history.Load());

            string id1 = history.AppendBatch(new[]
            {
                new CompressionHistoryEntry { FilePath = @"C:\a.bin", PreviousAlgorithm = "None", NewAlgorithm = "Ntfs" },
                new CompressionHistoryEntry { FilePath = @"C:\b.bin", PreviousAlgorithm = "None", NewAlgorithm = "Ntfs" }
            });
            string id2 = history.AppendBatch(new[]
            {
                new CompressionHistoryEntry { FilePath = @"C:\c.bin", PreviousAlgorithm = "Ntfs", NewAlgorithm = "Ntfs" }
            });

            Assert.NotEqual(id1, id2);
            Assert.Equal(3, history.Load().Count);

            var last = history.LoadLastBatch();
            Assert.Single(last);
            Assert.Equal(@"C:\c.bin", last[0].FilePath);
            Assert.Equal(id2, last[0].BatchId);

            history.RemoveBatch(id2);
            Assert.Equal(2, history.Load().Count);

            var lastNow = history.LoadLastBatch();
            Assert.Equal(2, lastNow.Count);
            Assert.Equal(id1, lastNow[0].BatchId);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void HistoryService_CapacityDropsOldestEntries()
    {
        string folder = NewHistoryFolder();
        try
        {
            var history = new JsonCompressionHistoryService(folder, capacity: 3);

            for (int i = 0; i < 5; i++)
            {
                history.AppendBatch(new[]
                {
                    new CompressionHistoryEntry { FilePath = $"C:\\f{i}.bin" }
                });

                history.AppendBatch(new[]
                {
                    new CompressionHistoryEntry { FilePath = $"C:\\g{i}.bin" }
                });
            }

            // Her batch 1 kayıttan ibaret; toplam 10, kapasite 3 → en yeni 3 kalır.
            var all = history.Load();
            Assert.Equal(3, all.Count);
            Assert.Equal(@"C:\g4.bin", all[^1].FilePath);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void HistoryService_CorruptFile_BehavesEmpty()
    {
        string folder = NewHistoryFolder();
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(System.IO.Path.Combine(folder, "history.json"), "bozuk {{{ json");

            var history = new JsonCompressionHistoryService(folder);

            Assert.Empty(history.Load());
            Assert.Empty(history.LoadLastBatch());
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // 5.4 Geri alma mantığı (kayıt hizmeti)
    // ------------------------------------------------------------------

    [Fact]
    public async Task RevertService_NonePrevious_OnlyDecompresses()
    {
        var compression = new RecordingCompression();
        var revert = new CompressionRevertService(compression);

        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("x.bin", 4096);

        var summary = await revert.RevertBatchAsync(
        [
            new CompressionHistoryEntry { FilePath = path, PreviousAlgorithm = "None", NewAlgorithm = "Ntfs", BatchId = "b1" }
        ]);

        Assert.Equal(1, summary.SuccessfulCount);
        Assert.Single(compression.Calls);
        Assert.Equal(CompressionAlgorithm.None, compression.Calls[0].Algorithm); // yalnız decompress
    }

    [Fact]
    public async Task RevertService_NtfsPrevious_DecompressesThenRecompresses()
    {
        var compression = new RecordingCompression();
        var revert = new CompressionRevertService(compression);

        using var tempDir = new TempTestDirectory();
        string path = tempDir.CreateFile("y.bin", 4096);

        var summary = await revert.RevertBatchAsync(
        [
            new CompressionHistoryEntry { FilePath = path, PreviousAlgorithm = "Ntfs", NewAlgorithm = "Lzx", BatchId = "b1" }
        ]);

        Assert.Equal(1, summary.SuccessfulCount);
        Assert.Equal(2, compression.Calls.Count);
        Assert.Equal(CompressionAlgorithm.None, compression.Calls[0].Algorithm);          // önce tamamen aç
        Assert.Equal(CompressionAlgorithm.Ntfs, compression.Calls[1].Algorithm);          // sonra eski algoritmayı uygula
        Assert.Equal("Ntfs", compression.Calls[1].Item.CurrentAlgorithm.ToString());
    }

    [Fact]
    public async Task RevertService_MissingFile_ReportsFailure()
    {
        var compression = new RecordingCompression();
        var revert = new CompressionRevertService(compression);

        var summary = await revert.RevertBatchAsync(
        [
            new CompressionHistoryEntry { FilePath = @"C:\yok_boyle_dosya_54321.bin", PreviousAlgorithm = "None", NewAlgorithm = "Ntfs" }
        ]);

        Assert.Equal(0, summary.SuccessfulCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Empty(compression.Calls);
    }

    // ------------------------------------------------------------------
    // 5.4 VM uçtan uca: sıkıştır → geçmişe yaz → geri al (gerçek compact.exe)
    // ------------------------------------------------------------------

    [Fact]
    public async Task VM_CompressThenRestore_RoundTripsRealFiles()
    {
        using var tempDir = new TempTestDirectory();
        tempDir.CreateFile("e2e_1.dat", 64 * 1024, fillByte: (byte)'Z');
        tempDir.CreateFile("e2e_2.dat", 64 * 1024, fillByte: (byte)'Z');

        string historyFolder = NewHistoryFolder();
        try
        {
            var vm = new MainViewModel(
                new FileScannerService(),
                new CompressionService(),
                new FakeAdmin { AdminStatus = false },
                historyService: new JsonCompressionHistoryService(historyFolder))
            {
                SelectedPath = tempDir.Path,
                SelectedAlgorithm = CompressionAlgorithm.Ntfs
            };

            await vm.ExecuteScanAsync();
            Assert.Equal(2, vm.Files.Count);
            Assert.False(vm.CanRestoreLastOperation);

            await vm.ExecuteCompressSelectedAsync();

            // Geçmişe 2 kayıt: önceki None, yeni Ntfs.
            var history = new JsonCompressionHistoryService(historyFolder);
            var lastBatch = history.LoadLastBatch();
            Assert.Equal(2, lastBatch.Count);
            Assert.All(lastBatch, e => Assert.Equal("None", e.PreviousAlgorithm));
            Assert.All(lastBatch, e => Assert.Equal("Ntfs", e.NewAlgorithm));
            Assert.True(vm.CanRestoreLastOperation);
            Assert.True(FilesAllCompressed(vm));

            // Geri al: dosyalar açılmalı, batch geçmişten düşmeli.
            await vm.ExecuteRestoreLastOperationAsync();

            Assert.False(vm.CanRestoreLastOperation);
            Assert.Empty(new JsonCompressionHistoryService(historyFolder).Load());
            Assert.True(FilesAllUncompressed(vm));
            Assert.Contains("Geri alma bitti", vm.ActivityLogs.Last().Message);

            Assert.True(vm.Files.All(f => f.AllocatedSize == f.Size),
                "Geri alınan dosyalar diskte orijinal boyutlarında olmalı.");
        }
        finally
        {
            if (Directory.Exists(historyFolder))
                Directory.Delete(historyFolder, recursive: true);
        }
    }

    private static bool FilesAllCompressed(MainViewModel vm) => vm.Files.All(f => f.IsCompressed);
    private static bool FilesAllUncompressed(MainViewModel vm) => vm.Files.All(f => !f.IsCompressed);

    // ------------------------------------------------------------------
    // 5.5 Oyun kütüphanesi tespiti
    // ------------------------------------------------------------------

    [Fact]
    public void GameLibraryService_DetectsSteamViaRegistry()
    {
        using var tempDir = new TempTestDirectory();
        string steamRoot = System.IO.Path.Combine(tempDir.Path, "Steam");
        string steamCommon = System.IO.Path.Combine(steamRoot, "steamapps", "common");
        Directory.CreateDirectory(steamCommon);

        var registry = new FakeRegistryReader();
        registry.Keys[@"SOFTWARE\WOW6432Node\Valve\Steam"] = new() { ["InstallPath"] = steamRoot };

        // directoryExists fake'i de şart: gerçek makinede EA Games / XboxGames
        // klasörleri kurulu olabilir — test yalnızca Steam adayını doğrulamalı.
        var service = new GameLibraryService(registry, directoryExists: p =>
            string.Equals(p, steamCommon, StringComparison.OrdinalIgnoreCase));
        var found = service.DetectInstalledLibraries();

        var steam = Assert.Single(found);
        Assert.Equal("Steam", steam.Name);
        Assert.Equal(steamCommon, steam.GamesPath);
        Assert.StartsWith("🎮", steam.DisplayName);
    }

    [Fact]
    public void GameLibraryService_UnknownRegistryKey_FallsBackToKnownPaths()
    {
        using var tempDir = new TempTestDirectory();

        // Registry boş; Epic varsayılan yolu yok → hiçbir şey bulunamaz.
        var service = new GameLibraryService(new FakeRegistryReader(), directoryExists: _ => false);
        Assert.Empty(service.DetectInstalledLibraries());

        // GOG klasörü "kurulu": klasör kontrolü ile bulunmalı.
        var found = new GameLibraryService(new FakeRegistryReader(), directoryExists: p => p == @"C:\GOG Games")
            .DetectInstalledLibraries();
        var gog = Assert.Single(found);
        Assert.Equal("GOG Galaxy", gog.Name);
    }

    // ------------------------------------------------------------------
    // 5.5 VM entegrasyonu
    // ------------------------------------------------------------------

    [Fact]
    public void VM_GameLibraries_PopulateAndSelect()
    {
        using var tempDir = new TempTestDirectory();

        var lib1 = new GameLibraryInfo("Steam", System.IO.Path.Combine(tempDir.Path, "SteamCommon"), "🎮");
        Directory.CreateDirectory(lib1.GamesPath);

        var vm = new MainViewModel(
            new FakeScanner(),
            new RecordingCompression(),
            new FakeAdmin(),
            gameLibraryService: new FakeGameLibraryService { Libraries = [lib1] });

        Assert.Single(vm.GameLibraries);
        Assert.Equal("Steam", vm.GameLibraries[0].Name);

        vm.SelectGameLibraryCommand.Execute(lib1.GamesPath);
        Assert.Equal(lib1.GamesPath, vm.SelectedPath);
        Assert.Contains("Oyun kütüphanesi seçildi", vm.ActivityLogs.Last().Message);
    }

    // ------------------------------------------------------------------
    // 5.1 Zamanlanmış görev
    // ------------------------------------------------------------------

    [Fact]
    public void ScheduledTaskService_BuildsCorrectSchtasksArguments()
    {
        var service = new ScheduledTaskService();
        var definition = new ScheduledTaskDefinition(
            "Games_Steam",
            @"C:\Tools\WinCompressor.exe",
            "cli compress \"C:\\Games\\Steam\" --algorithm lzx",
            DayOfWeek.Monday,
            new TimeOnly(3, 0));

        string args = service.BuildArguments(definition);

        Assert.StartsWith("/Create /F /TN \"WinCompressor\\Games_Steam\"", args);
        Assert.Contains("/TR \"\\\"C:\\Tools\\WinCompressor.exe\\\" cli compress \\\"C:\\Games\\Steam\\\" --algorithm lzx\"", args);
        Assert.EndsWith("/SC WEEKLY /D MON /ST 03:00", args);
    }

    [Fact]
    public void VM_ScheduleWeeklyTask_CapturesDefinitionWithCliArguments()
    {
        using var tempDir = new TempTestDirectory();

        var tasks = new FakeScheduledTaskService();
        var vm = new MainViewModel(
            new FakeScanner(),
            new RecordingCompression(),
            new FakeAdmin { AdminStatus = false },   // NTFS admin gerektirmez
            scheduledTaskService: tasks)
        {
            SelectedPath = tempDir.Path,
            SelectedAlgorithm = CompressionAlgorithm.Ntfs
        };

        vm.ScheduleWeeklyTaskCommand.Execute(null);

        Assert.NotNull(tasks.Captured);
        Assert.Contains("cli compress", tasks.Captured!.Arguments);
        Assert.Contains(tempDir.Path, tasks.Captured.Arguments);
        Assert.Contains("ntfs", tasks.Captured.Arguments);
        Assert.Equal(DayOfWeek.Monday, tasks.Captured.DayOfWeek);
        Assert.Equal(new TimeOnly(3, 0), tasks.Captured.Time);
        Assert.NotEmpty(tasks.Captured.ExecutablePath);
        Assert.Contains("Zamanlanmış görev oluşturuldu", vm.ActivityLogs.Last().Message);
    }

    [Fact]
    public void VM_ScheduleWeeklyTask_NonAdminWithAdminAlgorithm_IsRefused()
    {
        using var tempDir = new TempTestDirectory();

        var tasks = new FakeScheduledTaskService();
        var vm = new MainViewModel(
            new FakeScanner(),
            new RecordingCompression(),
            new FakeAdmin { AdminStatus = false },
            scheduledTaskService: tasks)
        {
            SelectedPath = tempDir.Path,
            SelectedAlgorithm = CompressionAlgorithm.Lzx   // admin-only, non-admin run
        };

        vm.ScheduleWeeklyTaskCommand.Execute(null);

        Assert.Null(tasks.Captured);   // görev oluşturulmadı
        Assert.Contains("yönetici", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTaskName_SanitizesInvalidCharacters()
    {
        // ':' ve '|' dosya adı için geçersiz; temizlenmiş isim kalmalı.
        string raw = "C:\\Games:|Steam";
        string name = MainViewModel.BuildTaskName(raw);

        Assert.NotEmpty(name);
        Assert.DoesNotContain(name, Path.GetInvalidFileNameChars());
    }

    // ------------------------------------------------------------------
    // 5.2 CLI
    // ------------------------------------------------------------------

    private static (int ExitCode, string Output, string Error) RunCli(
        IFileScannerService scanner,
        ICompressionService compression,
        ISavingsEstimationService? estimator,
        params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var dispatcher = new CliDispatcher(scanner, compression, estimator, output, error);
        int code = dispatcher.RunAsync(args).GetAwaiter().GetResult();

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void Cli_Help_ExitsZero()
    {
        var (code, output, _) = RunCli(new FakeScanner(), new RecordingCompression(), null, "--help");

        Assert.Equal(0, code);
        Assert.Contains("cli scan", output);
        Assert.Contains("cli compress", output);
    }

    [Fact]
    public void Cli_NoArgs_ExitsOne()
    {
        var (code, _, _) = RunCli(new FakeScanner(), new RecordingCompression(), null);

        Assert.Equal(1, code);
    }

    [Fact]
    public void Cli_UnknownAlgorithm_IsUsageError()
    {
        var (code, _, error) = RunCli(new FakeScanner(), new RecordingCompression(), null,
            "compress", @"C:\", "--algorithm", "bogus");

        Assert.Equal(1, code);
        Assert.Contains("algoritma", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_Scan_JsonOutputIsParseable()
    {
        using var tempDir = new TempTestDirectory();
        tempDir.CreateFile("one.dat", 8192);
        tempDir.CreateFile("two.dat", 8192);

        var (code, output, _) = RunCli(
            new FileScannerService(),
            new RecordingCompression(),
            null,
            "scan", tempDir.Path, "--json");

        Assert.Equal(0, code);

        int lastIndex = output.LastIndexOf(']');
        int firstIndex = output.IndexOf('[');
        Assert.True(firstIndex >= 0 && lastIndex > firstIndex, "Çıktıda JSON array beklenirdi.");

        using var doc = JsonDocument.Parse(output[firstIndex..(lastIndex + 1)]);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void Cli_Compress_PassesCorrectFilesAndAlgorithm()
    {
        using var tempDir = new TempTestDirectory();
        tempDir.CreateFile("c1.dat", 8192);
        tempDir.CreateFile("c2.dat", 8192);

        var compression = new RecordingCompression();
        var (code, output, _) = RunCli(
            new FileScannerService(),
            compression,
            null,
            "compress", tempDir.Path, "--algorithm", "xpress8k");

        // Fake başarılı döndürür → tam başarı beklenir; routing doğrulanır.
        Assert.Equal(0, code);
        Assert.Equal(2, compression.Calls.Count);
        Assert.All(compression.Calls, c => Assert.Equal(CompressionAlgorithm.Xpress8K, c.Algorithm));
        Assert.All(compression.Calls, c => Assert.Contains(tempDir.Path, c.Item.FilePath));
    }

    [Fact]
    public void Cli_AlgorithmNameParser()
    {
        Assert.Equal(CompressionAlgorithm.Ntfs, CliDispatcher.ParseAlgorithmName("NTFS"));
        Assert.Equal(CompressionAlgorithm.Lzx, CliDispatcher.ParseAlgorithmName("lzx"));
        Assert.Equal(CompressionAlgorithm.Xpress4K, CliDispatcher.ParseAlgorithmName("Xpress4K"));
        Assert.Equal(CompressionAlgorithm.Xpress16K, CliDispatcher.ParseAlgorithmName("xpress16k"));
        Assert.Null(CliDispatcher.ParseAlgorithmName("unknown"));
    }

    // ------------------------------------------------------------------
    // 5.3 Dil ayarı kalıcılığı
    // ------------------------------------------------------------------

    [Fact]
    public void Settings_Language_RoundtripsAndDefaultsToTurkish()
    {
        string folder = NewHistoryFolder();
        try
        {
            Assert.Equal("tr", new AppSettings().Language);

            // Varsayılan ayarlar pencere sınırları double.NaN içerir — Save
            // NaN yüzünden patlamamalı (JsonNumberHandling.AllowNamedFloatingPointLiterals).
            var service = new JsonSettingsService(folder);
            service.Save(new AppSettings { Language = "en" });
            var loaded = new JsonSettingsService(folder).Load();
            Assert.Equal("en", loaded.Language);
            Assert.True(double.IsNaN(loaded.WindowLeft), "NaN pencere sınırları gidiş-dönüşü korumalı.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
