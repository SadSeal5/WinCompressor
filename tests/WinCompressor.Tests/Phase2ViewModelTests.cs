using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

/// <summary>
/// Phase 2 UI-layer regressions: simulate (dry-run) command, estimate lifecycle,
/// algorithm column text, and the CompactOS category filter.
/// </summary>
public class Phase2ViewModelTests
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

    private class RecordingCompression : ICompressionService
    {
        public int CompressCalls { get; private set; }

        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default)
        {
            CompressCalls++;
            fileItem.AllocatedSize = fileItem.Size / 2;
            fileItem.IsCompressed = true;
            fileItem.CurrentAlgorithm = algorithm;
            fileItem.Status = "Compressed";
            return Task.FromResult(true);
        }

        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default)
        {
            fileItem.AllocatedSize = fileItem.Size;
            fileItem.IsCompressed = false;
            fileItem.CurrentAlgorithm = CompressionAlgorithm.None;
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
            CompressCalls++;
            var list = files.ToList();
            foreach (var item in list)
            {
                item.AllocatedSize = item.Size / 2;
                item.IsCompressed = true;
                item.CurrentAlgorithm = algorithm;
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
            foreach (var item in list)
            {
                item.AllocatedSize = item.Size;
                item.IsCompressed = false;
                item.CurrentAlgorithm = CompressionAlgorithm.None;
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

    private class FakeEstimator : ISavingsEstimationService
    {
        public long ResultPerFile { get; set; } = 400;
        public List<(string Path, long Size, CompressionAlgorithm Algorithm)> Calls { get; } = [];

        public Task<long> EstimateSavedBytesAsync(
            string filePath, long fileSize, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default)
        {
            Calls.Add((filePath, fileSize, algorithm));
            return Task.FromResult(ResultPerFile);
        }
    }

    private static MainViewModel CreateViewModel(FakeEstimator? estimator = null, bool isAdmin = true)
    {
        return new MainViewModel(
            new FakeScanner(),
            new RecordingCompression(),
            new FakeAdminService(isAdmin),
            estimator ?? new FakeEstimator());
    }

    private class FakeAdminService : IAdminService
    {
        private readonly bool _isAdmin;
        public FakeAdminService(bool isAdmin) => _isAdmin = isAdmin;
        public bool IsAdministrator() => _isAdmin;
        public bool RestartAsAdmin(string? arguments = null) => true;
    }

    private static FileItemViewModel AddItem(MainViewModel vm, string name, long size, CompressionAlgorithm algorithm = CompressionAlgorithm.None)
    {
        var model = new FileItemInfo
        {
            FilePath = $"C:\\test\\{name}",
            FileName = name,
            Extension = Path.GetExtension(name),
            Size = size,
            AllocatedSize = size,
            CurrentAlgorithm = algorithm
        };
        var item = vm.CreateFileItem(model);
        vm.Files.Add(item);
        vm.ApplyFilter();
        return item;
    }

    [Fact]
    public void AlgorithmText_MapsEveryAlgorithmToDisplayText()
    {
        var vm = CreateViewModel();

        Assert.Equal("—", AddItem(vm, "a.bin", 100, CompressionAlgorithm.None).AlgorithmText);
        Assert.Equal("NTFS", AddItem(vm, "b.bin", 100, CompressionAlgorithm.Ntfs).AlgorithmText);
        Assert.Equal("XPRESS 4K", AddItem(vm, "c.bin", 100, CompressionAlgorithm.Xpress4K).AlgorithmText);
        Assert.Equal("XPRESS 8K", AddItem(vm, "d.bin", 100, CompressionAlgorithm.Xpress8K).AlgorithmText);
        Assert.Equal("XPRESS 16K", AddItem(vm, "e.bin", 100, CompressionAlgorithm.Xpress16K).AlgorithmText);
        Assert.Equal("LZX", AddItem(vm, "f.bin", 100, CompressionAlgorithm.Lzx).AlgorithmText);
        Assert.Equal("Bilinmiyor", AddItem(vm, "g.bin", 100, CompressionAlgorithm.Unknown).AlgorithmText);
    }

    [Fact]
    public async Task SimulateSelected_FillsEstimatesReportsTotalsAndUsesSelectedAlgorithm()
    {
        var estimator = new FakeEstimator { ResultPerFile = 400 };
        var vm = CreateViewModel(estimator);
        var item = AddItem(vm, "game.dat", 1000);

        item.IsSelected = true;
        await vm.ExecuteSimulateSelectedAsync();

        Assert.Single(estimator.Calls);
        Assert.Equal(CompressionAlgorithm.Lzx, estimator.Calls[0].Algorithm); // admin default
        Assert.Equal(400, item.Model.EstimatedSavedBytes);
        Assert.StartsWith("~", item.EstimatedSavingsText);
        Assert.Contains("Simülasyon tamamlandı", vm.StatusMessage);
        Assert.Contains("400 B", vm.StatusMessage); // formatted total of one estimate
    }

    [Fact]
    public async Task SimulateSelected_WithoutSelection_DoesNothing()
    {
        var estimator = new FakeEstimator();
        var vm = CreateViewModel(estimator);
        var item = AddItem(vm, "game.dat", 1000);
        item.IsSelected = false;

        await vm.ExecuteSimulateSelectedAsync();

        Assert.Empty(estimator.Calls);
        Assert.Contains("en az bir dosya", vm.StatusMessage);
    }

    [Fact]
    public async Task Estimates_AreClearedAfterRealCompression()
    {
        var estimator = new FakeEstimator { ResultPerFile = 400 };
        var vm = CreateViewModel(estimator);
        var item = AddItem(vm, "game.dat", 1000);

        item.IsSelected = true;
        await vm.ExecuteSimulateSelectedAsync();
        Assert.NotNull(item.Model.EstimatedSavedBytes);

        await vm.ExecuteCompressSelectedAsync();

        // The stale dry-run estimate must be dropped after the real compression.
        Assert.Null(item.Model.EstimatedSavedBytes);
        Assert.Equal("-", item.EstimatedSavingsText);
    }

    [Fact]
    public void CompactOsCategory_FiltersOnlyWofCompressedFiles()
    {
        var vm = CreateViewModel();

        var wofLzx = AddItem(vm, "wof_lzx.pak", 5000, CompressionAlgorithm.Lzx);
        AddItem(vm, "wof_xpress.pak", 5000, CompressionAlgorithm.Xpress16K);
        AddItem(vm, "ntfs_file.bin", 5000, CompressionAlgorithm.Ntfs);
        AddItem(vm, "plain.bin", 5000, CompressionAlgorithm.None);
        AddItem(vm, "unknown.bin", 5000, CompressionAlgorithm.Unknown);

        vm.SelectedCategory = FileCategory.CompactOs;

        var filtered = vm.FilesView.Cast<FileItemViewModel>().ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains(wofLzx, filtered);
        Assert.Contains(filtered, f => f.CurrentAlgorithm == CompressionAlgorithm.Xpress16K);
        Assert.Equal(2, vm.FilteredFilesCount);
    }
}
