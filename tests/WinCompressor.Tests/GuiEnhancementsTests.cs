using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

public class GuiEnhancementsTests
{
    private class FakeScanner : IFileScannerService
    {
        public List<FileItemInfo> ResultsToReturn { get; set; } = [];
        public Task<IReadOnlyList<FileItemInfo>> ScanAsync(
            ScanOptions options,
            IProgress<ScanProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FileItemInfo>>(ResultsToReturn);
        }

        public bool IsRootDrive(string path) => path.EndsWith(":\\") || path.EndsWith(":/");
    }

    private class FakeCompression : ICompressionService
    {
        public List<FileItemInfo> CompressedFiles { get; } = [];
        public List<FileItemInfo> DecompressedFiles { get; } = [];

        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default)
        {
            CompressedFiles.Add(fileItem);
            fileItem.AllocatedSize = fileItem.Size / 2;
            fileItem.IsCompressed = true;
            fileItem.Status = "Compressed";
            return Task.FromResult(true);
        }

        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default)
        {
            DecompressedFiles.Add(fileItem);
            fileItem.AllocatedSize = fileItem.Size;
            fileItem.IsCompressed = false;
            fileItem.Status = "Decompressed";
            return Task.FromResult(true);
        }

        public Task<CompressionResultSummary> CompressFilesAsync(
            IEnumerable<FileItemInfo> files,
            CompressionAlgorithm algorithm,
            int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var list = files.ToList();
            CompressedFiles.AddRange(list);
            foreach (var item in list)
            {
                item.AllocatedSize = item.Size / 2;
                item.IsCompressed = true;
                item.Status = "Compressed";
            }
            return Task.FromResult(new CompressionResultSummary
            {
                TotalFiles = list.Count,
                SuccessfulCount = list.Count,
                Items = list
            });
        }

        public Task<CompressionResultSummary> DecompressFilesAsync(
            IEnumerable<FileItemInfo> files,
            int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var list = files.ToList();
            DecompressedFiles.AddRange(list);
            foreach (var item in list)
            {
                item.AllocatedSize = item.Size;
                item.IsCompressed = false;
                item.Status = "Decompressed";
            }
            return Task.FromResult(new CompressionResultSummary
            {
                TotalFiles = list.Count,
                SuccessfulCount = list.Count,
                Items = list
            });
        }
    }

    private class FakeAdmin : IAdminService
    {
        public bool IsAdministrator() => true;
        public bool RestartAsAdmin(string? arguments = null) => true;
    }

    private static MainViewModel CreateViewModelWithItems(params (string name, string ext, long size, string path)[] fileSpecs)
    {
        var scanner = new FakeScanner();
        var compression = new FakeCompression();
        var admin = new FakeAdmin();

        var vm = new MainViewModel(scanner, compression, admin);

        foreach (var (name, ext, size, path) in fileSpecs)
        {
            var model = new FileItemInfo
            {
                FileName = name,
                Extension = ext,
                Size = size,
                AllocatedSize = size,
                FilePath = path,
                IsCompressed = false
            };
            vm.Files.Add(vm.CreateFileItem(model));
        }

        vm.UpdateStorageMetrics();
        vm.ApplyFilter();
        return vm;
    }

    [Fact]
    public void SearchFilter_MatchesFileName_CaseInsensitive()
    {
        var vm = CreateViewModelWithItems(
            ("Cyberpunk2077.exe", ".exe", 1000, "C:\\Games\\Cyberpunk2077.exe"),
            ("readme.txt", ".txt", 200, "C:\\Games\\readme.txt"),
            ("config.json", ".json", 500, "C:\\Games\\config.json")
        );

        Assert.Equal(3, vm.FilteredFilesCount);

        // Search for "cyber"
        vm.SearchText = "cyber";
        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();
        Assert.Single(filtered);
        Assert.Equal("Cyberpunk2077.exe", filtered[0].FileName);
        Assert.Equal(1, vm.FilteredFilesCount);

        // Clear search
        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(3, vm.FilteredFilesCount);
    }

    [Fact]
    public void SearchFilter_MatchesExtension()
    {
        var vm = CreateViewModelWithItems(
            ("app.exe", ".exe", 1000, "C:\\Bin\\app.exe"),
            ("helper.dll", ".dll", 2000, "C:\\Bin\\helper.dll"),
            ("notes.txt", ".txt", 100, "C:\\Bin\\notes.txt")
        );

        vm.SearchText = "dll";
        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();
        Assert.Single(filtered);
        Assert.Equal("helper.dll", filtered[0].FileName);
    }

    [Fact]
    public void SearchFilter_MatchesFilePathDirectory()
    {
        var vm = CreateViewModelWithItems(
            ("engine.dll", ".dll", 5000, "D:\\Engines\\Unreal\\engine.dll"),
            ("unity.dll", ".dll", 4000, "D:\\Engines\\Unity\\unity.dll"),
            ("script.cs", ".cs", 300, "C:\\Projects\\App\\script.cs")
        );

        vm.SearchText = "Unreal";
        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();
        Assert.Single(filtered);
        Assert.Equal("engine.dll", filtered[0].FileName);
    }

    [Fact]
    public void CategoryFilter_Executables_MatchesOnlyExpectedExtensions()
    {
        var vm = CreateViewModelWithItems(
            ("game.exe", ".exe", 1000, "C:\\game.exe"),
            ("core.dll", ".dll", 2000, "C:\\core.dll"),
            ("driver.sys", ".sys", 500, "C:\\driver.sys"),
            ("active.ocx", ".ocx", 400, "C:\\active.ocx"),
            ("script.py", ".py", 100, "C:\\script.py"),
            ("archive.zip", ".zip", 5000, "C:\\archive.zip")
        );

        vm.SelectedCategory = FileCategory.Executables;

        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();
        Assert.Equal(4, filtered.Count);
        Assert.All(filtered, item => Assert.Contains(item.Extension.ToLowerInvariant(), new[] { ".exe", ".dll", ".sys", ".ocx" }));
    }

    [Fact]
    public void CategoryFilter_LargeFiles_MatchesFilesOver50MB()
    {
        const long fiftyMB = 50L * 1024L * 1024L;
        const long fortyNineMB = 49L * 1024L * 1024L;
        const long oneHundredMB = 100L * 1024L * 1024L;

        var vm = CreateViewModelWithItems(
            ("huge.iso", ".iso", oneHundredMB, "C:\\huge.iso"),
            ("exact.dat", ".dat", fiftyMB, "C:\\exact.dat"),
            ("smaller.bin", ".bin", fortyNineMB, "C:\\smaller.bin"),
            ("tiny.txt", ".txt", 1024, "C:\\tiny.txt")
        );

        vm.SelectedCategory = FileCategory.LargeFiles;

        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();
        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, f => f.FileName == "huge.iso");
        Assert.Contains(filtered, f => f.FileName == "exact.dat");
        Assert.DoesNotContain(filtered, f => f.FileName == "smaller.bin");
    }

    [Fact]
    public void CategoryFilter_TextCode_MatchesSourceAndTextExtensions()
    {
        var vm = CreateViewModelWithItems(
            ("Program.cs", ".cs", 500, "C:\\Program.cs"),
            ("config.json", ".json", 200, "C:\\config.json"),
            ("style.css", ".css", 100, "C:\\style.css"),
            ("data.sqlite", ".sqlite", 4000, "C:\\data.sqlite"),
            ("app.exe", ".exe", 8000, "C:\\app.exe")
        );

        vm.SelectedCategory = FileCategory.TextCode;

        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();
        Assert.Equal(3, filtered.Count);
        Assert.Contains(filtered, f => f.FileName == "Program.cs");
        Assert.Contains(filtered, f => f.FileName == "config.json");
        Assert.Contains(filtered, f => f.FileName == "style.css");
    }

    [Fact]
    public void CategoryFilter_DataMedia_MatchesDatabaseAndMedia()
    {
        var vm = CreateViewModelWithItems(
            ("game.pak", ".pak", 20000, "C:\\game.pak"),
            ("data.bin", ".bin", 15000, "C:\\data.bin"),
            ("store.db", ".db", 8000, "C:\\store.db"),
            ("disk.vmdk", ".vmdk", 50000, "C:\\disk.vmdk"),
            ("image.iso", ".iso", 90000, "C:\\image.iso"),
            ("notes.txt", ".txt", 100, "C:\\notes.txt")
        );

        vm.SetCategory("DataMedia");

        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();
        Assert.Equal(5, filtered.Count);
        Assert.DoesNotContain(filtered, f => f.FileName == "notes.txt");
    }

    [Fact]
    public void CategoryFilter_And_SearchText_CombineCorrectly()
    {
        var vm = CreateViewModelWithItems(
            ("EngineCore.dll", ".dll", 5000, "C:\\EngineCore.dll"),
            ("GameLauncher.exe", ".exe", 2000, "C:\\GameLauncher.exe"),
            ("GameData.pak", ".pak", 3000, "C:\\GameData.pak"),
            ("readme.txt", ".txt", 100, "C:\\readme.txt")
        );

        // Filter: Executables AND Search: "Launcher"
        vm.SelectedCategory = FileCategory.Executables;
        vm.SearchText = "Launcher";

        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();
        Assert.Single(filtered);
        Assert.Equal("GameLauncher.exe", filtered[0].FileName);
    }

    [Fact]
    public void AdvancedSelection_InvertSelection_InvertsAllFiles()
    {
        var vm = CreateViewModelWithItems(
            ("f1.bin", ".bin", 1000, "C:\\f1.bin"),
            ("f2.bin", ".bin", 2000, "C:\\f2.bin"),
            ("f3.bin", ".bin", 3000, "C:\\f3.bin")
        );

        // Initially select f1 only
        vm.Files[0].IsSelected = true;
        vm.Files[1].IsSelected = false;
        vm.Files[2].IsSelected = false;
        Assert.Equal(1, vm.SelectedFilesCount);

        // Invert
        vm.InvertSelectionCommand.Execute(null);

        Assert.False(vm.Files[0].IsSelected);
        Assert.True(vm.Files[1].IsSelected);
        Assert.True(vm.Files[2].IsSelected);
        Assert.Equal(2, vm.SelectedFilesCount);
        Assert.Equal(5000, vm.SelectedFilesTotalSize);
    }

    [Fact]
    public void AdvancedSelection_SelectLargeFiles_SelectsOnly50MBPlus()
    {
        const long fiftyMB = 50L * 1024L * 1024L;
        const long tenMB = 10L * 1024L * 1024L;

        var vm = CreateViewModelWithItems(
            ("small.bin", ".bin", tenMB, "C:\\small.bin"),
            ("large.bin", ".bin", fiftyMB, "C:\\large.bin")
        );

        vm.SelectLargeFilesCommand.Execute(null);

        Assert.False(vm.Files[0].IsSelected);
        Assert.True(vm.Files[1].IsSelected);
        Assert.Equal(1, vm.SelectedFilesCount);
        Assert.Equal(fiftyMB, vm.SelectedFilesTotalSize);
    }

    [Fact]
    public void ContextMenu_ExcludeFromList_RemovesItemAndRecalculatesMetrics()
    {
        var vm = CreateViewModelWithItems(
            ("keep.bin", ".bin", 1000, "C:\\keep.bin"),
            ("remove.bin", ".bin", 2000, "C:\\remove.bin")
        );

        Assert.Equal(2, vm.Files.Count);
        Assert.Equal(3000, vm.TotalUncompressedBytes);

        var itemToRemove = vm.Files[1];
        vm.ExcludeFromListCommand.Execute(itemToRemove);

        Assert.Single(vm.Files);
        Assert.Equal("keep.bin", vm.Files[0].FileName);
        Assert.Equal(1000, vm.TotalUncompressedBytes);
        Assert.Contains("remove.bin", vm.ActivityLogs.Last().Message);
    }

    [Fact]
    public void ContextMenu_OpenFileLocation_CallsHandler()
    {
        var vm = CreateViewModelWithItems(
            ("app.exe", ".exe", 1000, "C:\\TestApp\\app.exe")
        );

        string? openedPath = null;
        vm.OpenFileLocationHandler = path => openedPath = path;

        vm.OpenFileLocationCommand.Execute(vm.Files[0]);

        Assert.Equal("C:\\TestApp\\app.exe", openedPath);
        Assert.Contains("app.exe", vm.ActivityLogs.Last().Message);
    }

    [Fact]
    public void ContextMenu_CopyPath_CallsClipboardHandler()
    {
        var vm = CreateViewModelWithItems(
            ("app.exe", ".exe", 1000, "C:\\TestApp\\app.exe")
        );

        string? copiedText = null;
        vm.ClipboardHandler = text => copiedText = text;

        vm.CopyPathCommand.Execute(vm.Files[0]);

        Assert.Equal("C:\\TestApp\\app.exe", copiedText);
        Assert.Contains("C:\\TestApp\\app.exe", vm.ActivityLogs.Last().Message);
    }

    [Fact]
    public async Task ContextMenu_CompressSingleFile_CompressesOnlyTarget()
    {
        var scanner = new FakeScanner();
        var compression = new FakeCompression();
        var admin = new FakeAdmin();

        var vm = new MainViewModel(scanner, compression, admin);

        var m1 = new FileItemInfo { FileName = "one.bin", FilePath = "C:\\one.bin", Size = 2000, AllocatedSize = 2000 };
        var m2 = new FileItemInfo { FileName = "two.bin", FilePath = "C:\\two.bin", Size = 4000, AllocatedSize = 4000 };

        var item1 = vm.CreateFileItem(m1);
        var item2 = vm.CreateFileItem(m2);

        vm.Files.Add(item1);
        vm.Files.Add(item2);
        vm.UpdateStorageMetrics();

        // Compress item2 only
        await vm.ExecuteCompressSingleFileAsync(item2);

        Assert.Single(compression.CompressedFiles);
        Assert.Equal("C:\\two.bin", compression.CompressedFiles[0].FilePath);
        Assert.True(item2.IsCompressed);
        Assert.Equal(2000, item2.AllocatedSize); // 4000 / 2
        Assert.False(item1.IsCompressed);
        Assert.Equal(2000, item1.AllocatedSize);

        // Savings should reflect 2000 bytes saved
        Assert.Equal(2000, vm.TotalSavedBytes);
    }

    [Fact]
    public async Task ContextMenu_DecompressSingleFile_DecompressesOnlyTarget()
    {
        var scanner = new FakeScanner();
        var compression = new FakeCompression();
        var admin = new FakeAdmin();

        var vm = new MainViewModel(scanner, compression, admin);

        var m = new FileItemInfo
        {
            FileName = "comp.bin",
            FilePath = "C:\\comp.bin",
            Size = 4000,
            AllocatedSize = 2000,
            IsCompressed = true
        };
        var item = vm.CreateFileItem(m);
        vm.Files.Add(item);
        vm.UpdateStorageMetrics();

        await vm.ExecuteDecompressSingleFileAsync(item);

        Assert.Single(compression.DecompressedFiles);
        Assert.False(item.IsCompressed);
        Assert.Equal(4000, item.AllocatedSize);
        Assert.Equal(0, vm.TotalSavedBytes);
    }

    [Fact]
    public void StorageBarMetrics_CalculatesCorrectPercentagesAndRatios()
    {
        var vm = CreateViewModelWithItems();

        // Empty state
        Assert.Equal(0, vm.TotalUncompressedBytes);
        Assert.Equal(0, vm.TotalCompressedBytes);
        Assert.Equal(0, vm.TotalSavedBytes);
        Assert.Equal(0.0, vm.SavingsPercentage);
        Assert.Equal("%0 Alan Tasarrufu", vm.SavingsPercentageText);

        // Add an item with 100MB original, 40MB allocated (60MB saved = 60% savings)
        const long original = 100L * 1024L * 1024L;
        const long allocated = 40L * 1024L * 1024L;

        var model = new FileItemInfo
        {
            FileName = "test.dat",
            FilePath = "C:\\test.dat",
            Size = original,
            AllocatedSize = allocated,
            IsCompressed = true
        };
        vm.Files.Add(vm.CreateFileItem(model));
        vm.UpdateStorageMetrics();

        Assert.Equal(original, vm.TotalUncompressedBytes);
        Assert.Equal(allocated, vm.TotalCompressedBytes);
        Assert.Equal(original - allocated, vm.TotalSavedBytes);
        Assert.Equal(60.0, vm.SavingsPercentage, 1);
        Assert.Equal("-%60.0 Alan Tasarrufu", vm.SavingsPercentageText);

        // Verify Star ratios
        Assert.Equal(allocated, vm.CompressedWidthRatio.Value);
        Assert.Equal(original - allocated, vm.SavedWidthRatio.Value);
    }

    [Fact]
    public void DragAndDrop_DirectorySetsSelectedPathAndLogs()
    {
        var vm = CreateViewModelWithItems();
        var tempDir = Path.GetTempPath();

        vm.HandleDroppedPath(tempDir);

        Assert.Equal(tempDir, vm.SelectedPath);
        Assert.Contains(tempDir, vm.StatusMessage);
        Assert.Contains("Sürükle-bırak", vm.ActivityLogs.Last().Message);
    }

    [Fact]
    public void ActivityLogs_CanBeLoggedToggledAndCleared()
    {
        var vm = CreateViewModelWithItems();
        int initialCount = vm.ActivityLogs.Count;

        vm.LogActivity("Özel test log mesajı");
        Assert.Equal(initialCount + 1, vm.ActivityLogs.Count);
        Assert.Contains("Özel test log mesajı", vm.ActivityLogs.Last().Message);

        // Toggle visibility
        Assert.False(vm.IsActivityLogVisible);
        vm.ToggleActivityLogCommand.Execute(null);
        Assert.True(vm.IsActivityLogVisible);
        vm.ToggleActivityLogCommand.Execute(null);
        Assert.False(vm.IsActivityLogVisible);

        // Clear logs
        vm.ClearActivityLogsCommand.Execute(null);
        Assert.Empty(vm.ActivityLogs);
    }
}
