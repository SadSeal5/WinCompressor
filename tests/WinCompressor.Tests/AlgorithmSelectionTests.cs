using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

/// <summary>
/// Phase 1 regressions: algorithm card single-source (AlgorithmOptions),
/// admin gating for CompactOS algorithms, and selection sync.
/// </summary>
public class AlgorithmSelectionTests
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
        public int DecompressCalls { get; private set; }

        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default)
        {
            CompressCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default)
        {
            DecompressCalls++;
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
            return Task.FromResult(new CompressionResultSummary { Items = files.ToList() });
        }

        public Task<CompressionResultSummary> DecompressFilesAsync(
            IEnumerable<FileItemInfo> files,
            int maxDegreeOfParallelism = 4,
            IProgress<CompressionProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DecompressCalls++;
            return Task.FromResult(new CompressionResultSummary { Items = files.ToList() });
        }
    }

    private class FakeAdmin : IAdminService
    {
        public bool AdminStatus { get; set; }
        public bool IsAdministrator() => AdminStatus;
        public bool RestartAsAdmin(string? arguments = null) => true;
    }

    private static MainViewModel CreateViewModel(bool isAdmin, FakeScanner? scanner = null)
    {
        return new MainViewModel(scanner ?? new FakeScanner(), new RecordingCompression(), new FakeAdmin { AdminStatus = isAdmin });
    }

    [Fact]
    public void AlgorithmOptions_ContainAllFiveAlgorithms_ExactlyOnce()
    {
        var vm = CreateViewModel(isAdmin: true);

        var algorithms = vm.AlgorithmOptions.Select(o => o.Algorithm).ToList();

        Assert.Equal(5, algorithms.Count);
        Assert.Contains(CompressionAlgorithm.Lzx, algorithms);
        Assert.Contains(CompressionAlgorithm.Xpress4K, algorithms);
        Assert.Contains(CompressionAlgorithm.Xpress8K, algorithms);
        Assert.Contains(CompressionAlgorithm.Xpress16K, algorithms);
        Assert.Contains(CompressionAlgorithm.Ntfs, algorithms);
    }

    [Fact]
    public void Admin_AllAlgorithmOptionsAreEnabled()
    {
        var vm = CreateViewModel(isAdmin: true);

        Assert.All(vm.AlgorithmOptions, o => Assert.True(o.IsEnabled));
    }

    [Fact]
    public void NonAdmin_CompactOsOptionsDisabledAndNtfsEnabled()
    {
        var vm = CreateViewModel(isAdmin: false);

        Assert.All(
            vm.AlgorithmOptions.Where(o => o.RequiresAdmin),
            o => Assert.False(o.IsEnabled));
        Assert.True(vm.AlgorithmOptions.Single(o => o.Algorithm == CompressionAlgorithm.Ntfs).IsEnabled);
    }

    [Fact]
    public void InitialSelection_MarksExactlyOneOptionSelected()
    {
        var vmAdmin = CreateViewModel(isAdmin: true);
        var selectedAdmin = vmAdmin.AlgorithmOptions.Single(o => o.IsSelected);
        Assert.Equal(CompressionAlgorithm.Lzx, selectedAdmin.Algorithm);
        Assert.Same(
            vmAdmin.AlgorithmOptions.Single(o => o.Algorithm == vmAdmin.SelectedAlgorithm),
            selectedAdmin);

        var vmStandard = CreateViewModel(isAdmin: false);
        var selectedStandard = vmStandard.AlgorithmOptions.Single(o => o.IsSelected);
        Assert.Equal(CompressionAlgorithm.Ntfs, selectedStandard.Algorithm);
        Assert.Equal(CompressionAlgorithm.Ntfs, vmStandard.SelectedAlgorithm);
    }

    [Fact]
    public void SettingSelectedAlgorithm_KeepsOptionsInSync()
    {
        var vm = CreateViewModel(isAdmin: true);

        vm.SelectedAlgorithm = CompressionAlgorithm.Xpress16K;

        var selected = vm.AlgorithmOptions.Single(o => o.IsSelected);
        Assert.Equal(CompressionAlgorithm.Xpress16K, selected.Algorithm);
    }

    [Fact]
    public void SelectingAnOption_UpdatesSelectedAlgorithm_WithoutRecursionLoop()
    {
        var vm = CreateViewModel(isAdmin: true);

        vm.AlgorithmOptions.Single(o => o.Algorithm == CompressionAlgorithm.Xpress4K).IsSelected = true;

        Assert.Equal(CompressionAlgorithm.Xpress4K, vm.SelectedAlgorithm);
        // Exactly one option stays selected after the two-way sync cycle.
        Assert.Single(vm.AlgorithmOptions, o => o.IsSelected);
    }

    [Fact]
    public void NonAdmin_CanUseSelectedAlgorithm_FalseForWof_TrueForNtfs()
    {
        var vm = CreateViewModel(isAdmin: false);

        foreach (var wof in new[] { CompressionAlgorithm.Lzx, CompressionAlgorithm.Xpress4K, CompressionAlgorithm.Xpress8K, CompressionAlgorithm.Xpress16K })
        {
            vm.SelectedAlgorithm = wof;
            Assert.False(vm.CanUseSelectedAlgorithm, $"{wof} requires admin and must be refused for standard users");
        }

        vm.SelectedAlgorithm = CompressionAlgorithm.Ntfs;
        Assert.True(vm.CanUseSelectedAlgorithm);
    }

    [Fact]
    public async Task NonAdmin_CompressSelectedWithLzx_IsRefusedWithoutCompressing()
    {
        var compression = new RecordingCompression();
        var scanner = new FakeScanner
        {
            ResultsToReturn =
            [
                new FileItemInfo { FilePath = "C:\\game.exe", FileName = "game.exe", Extension = ".exe", Size = 1000, AllocatedSize = 1000 }
            ]
        };
        var vm = new MainViewModel(scanner, compression, new FakeAdmin { AdminStatus = false })
        {
            SelectedPath = "C:\\Games"
        };

        await vm.ExecuteScanAsync();
        vm.SelectedAlgorithm = CompressionAlgorithm.Lzx;
        Assert.Single(vm.Files.Where(f => f.IsSelected).ToList());

        await vm.ExecuteCompressSelectedAsync();

        Assert.Equal(0, compression.CompressCalls);
        Assert.Contains("yönetici", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("yönetici", vm.ActivityLogs.Last().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonAdmin_CompressSingleFileWithXpress_IsRefusedWithoutCompressing()
    {
        var compression = new RecordingCompression();
        var vm = new MainViewModel(new FakeScanner(), compression, new FakeAdmin { AdminStatus = false });
        vm.SelectedAlgorithm = CompressionAlgorithm.Xpress8K;

        var item = vm.CreateFileItem(new FileItemInfo
        {
            FilePath = "C:\\data.bin",
            FileName = "data.bin",
            Extension = ".bin",
            Size = 2000,
            AllocatedSize = 2000
        });
        vm.Files.Add(item);

        await vm.ExecuteCompressSingleFileAsync(item);

        Assert.Equal(0, compression.CompressCalls);
        Assert.Contains("yönetici", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_CompressSelectedWithLzx_IsAllowed()
    {
        var compression = new RecordingCompression();
        var scanner = new FakeScanner
        {
            ResultsToReturn =
            [
                new FileItemInfo { FilePath = "C:\\game.exe", FileName = "game.exe", Extension = ".exe", Size = 1000, AllocatedSize = 1000 }
            ]
        };
        var vm = new MainViewModel(scanner, compression, new FakeAdmin { AdminStatus = true })
        {
            SelectedPath = "C:\\Games"
        };

        await vm.ExecuteScanAsync();
        await vm.ExecuteCompressSelectedAsync();

        Assert.Equal(1, compression.CompressCalls);
    }
}
