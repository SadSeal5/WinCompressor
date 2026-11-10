using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

public class MainViewModelTests
{
    private class FakeScanner : IFileScannerService
    {
        public List<FileItemInfo> ResultsToReturn { get; set; } = [];
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<FileItemInfo>> ScanAsync(
            ScanOptions options,
            IProgress<ScanProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<FileItemInfo>>(ResultsToReturn);
        }

        public bool IsRootDrive(string path) => path.EndsWith(":\\") || path.EndsWith(":/");
    }

    private class FakeCompression : ICompressionService
    {
        public bool CompressCalled { get; private set; }
        public bool DecompressCalled { get; private set; }
        public List<FileItemInfo> CompressedItems { get; } = [];

        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default)
        {
            CompressCalled = true;
            fileItem.AllocatedSize = fileItem.Size / 2;
            fileItem.IsCompressed = true;
            fileItem.Status = "Compressed";
            return Task.FromResult(true);
        }

        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default)
        {
            DecompressCalled = true;
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
            CompressCalled = true;
            var list = files.ToList();
            CompressedItems.AddRange(list);
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
            DecompressCalled = true;
            var list = files.ToList();
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
        public bool AdminStatus { get; set; } = true;
        public bool RestartCalled { get; private set; }

        public bool IsAdministrator() => AdminStatus;
        public bool RestartAsAdmin(string? arguments = null)
        {
            RestartCalled = true;
            return true;
        }
    }

    [Fact]
    public void InitialState_ReflectsAdminAndDefaultAlgorithm()
    {
        var scanner = new FakeScanner();
        var compression = new FakeCompression();
        var admin = new FakeAdmin { AdminStatus = true };

        var vmAdmin = new MainViewModel(scanner, compression, admin);
        Assert.True(vmAdmin.IsAdministrator);
        Assert.Equal(CompressionAlgorithm.Lzx, vmAdmin.SelectedAlgorithm);

        admin.AdminStatus = false;
        var vmStandard = new MainViewModel(scanner, compression, admin);
        Assert.False(vmStandard.IsAdministrator);
        Assert.Equal(CompressionAlgorithm.Ntfs, vmStandard.SelectedAlgorithm);
    }

    [Fact]
    public async Task ScanAsync_PopulatesFilesStrictlySortedDescendingBySize()
    {
        var scanner = new FakeScanner
        {
            // Intentionally unsorted input
            ResultsToReturn =
            [
                new FileItemInfo { FilePath = "C:\\f1.bin", FileName = "f1.bin", Size = 500, AllocatedSize = 500 },
                new FileItemInfo { FilePath = "C:\\f2.bin", FileName = "f2.bin", Size = 20000, AllocatedSize = 20000 },
                new FileItemInfo { FilePath = "C:\\f3.bin", FileName = "f3.bin", Size = 5000, AllocatedSize = 5000 },
                new FileItemInfo { FilePath = "C:\\f4.bin", FileName = "f4.bin", Size = 100, AllocatedSize = 100 }
            ]
        };
        var compression = new FakeCompression();
        var admin = new FakeAdmin();

        var vm = new MainViewModel(scanner, compression, admin)
        {
            SelectedPath = "C:\\test"
        };

        await vm.ExecuteScanAsync();

        // INVARIANT VERIFICATION: Descending size order
        Assert.Equal(4, vm.Files.Count);
        Assert.Equal(20000, vm.Files[0].Size);
        Assert.Equal(5000, vm.Files[1].Size);
        Assert.Equal(500, vm.Files[2].Size);
        Assert.Equal(100, vm.Files[3].Size);

        // INVARIANT VERIFICATION: Scan is 100% read-only, never compresses
        Assert.False(compression.CompressCalled);
    }

    [Fact]
    public async Task Selection_And_CompressionCommand_CompressesOnlyCheckedItems()
    {
        var scanner = new FakeScanner
        {
            ResultsToReturn =
            [
                new FileItemInfo { FilePath = "C:\\a.txt", FileName = "a.txt", Size = 1000, AllocatedSize = 1000 },
                new FileItemInfo { FilePath = "C:\\b.txt", FileName = "b.txt", Size = 800, AllocatedSize = 800 }
            ]
        };
        var compression = new FakeCompression();
        var admin = new FakeAdmin();

        var vm = new MainViewModel(scanner, compression, admin)
        {
            SelectedPath = "C:\\test"
        };

        await vm.ExecuteScanAsync();

        // By default, both uncompressed are selected
        Assert.Equal(2, vm.SelectedFilesCount);
        Assert.Equal(1800, vm.SelectedFilesTotalSize);
        Assert.True(vm.IsAllSelected);

        // Deselect the second item
        vm.Files[1].IsSelected = false;
        Assert.Equal(1, vm.SelectedFilesCount);
        Assert.Equal(1000, vm.SelectedFilesTotalSize);
        Assert.Null(vm.IsAllSelected); // Mixed state

        // Execute compression
        await vm.ExecuteCompressSelectedAsync();

        Assert.True(compression.CompressCalled);
        Assert.Single(compression.CompressedItems);
        Assert.Equal("C:\\a.txt", compression.CompressedItems[0].FilePath);

        // Stats should update (Item 1: 1000 original, 500 allocated -> 500 saved)
        Assert.Equal(500, vm.TotalBytesSaved);
    }

    [Fact]
    public void QuickSelectDriveCommand_SetsPathWithoutCompression()
    {
        var scanner = new FakeScanner();
        var compression = new FakeCompression();
        var admin = new FakeAdmin();

        var vm = new MainViewModel(scanner, compression, admin);

        vm.QuickSelectDriveCommand.Execute("D:\\");
        Assert.Equal("D:\\", vm.SelectedPath);

        // INVARIANT: Drive is NEVER compressed
        Assert.False(compression.CompressCalled);
    }

    [Fact]
    public async Task SelectAllAndDeselectAll_ToggleCollectionState()
    {
        var scanner = new FakeScanner
        {
            ResultsToReturn =
            [
                new FileItemInfo { FilePath = "C:\\a.txt", FileName = "a.txt", Size = 1000, AllocatedSize = 1000 },
                new FileItemInfo { FilePath = "C:\\b.txt", FileName = "b.txt", Size = 800, AllocatedSize = 800 }
            ]
        };
        var compression = new FakeCompression();
        var admin = new FakeAdmin();

        var vm = new MainViewModel(scanner, compression, admin);
        await vm.ExecuteScanAsync();

        vm.DeselectAllCommand.Execute(null);
        Assert.Equal(0, vm.SelectedFilesCount);
        Assert.False(vm.IsAllSelected);

        vm.SelectAllCommand.Execute(null);
        Assert.Equal(2, vm.SelectedFilesCount);
        Assert.True(vm.IsAllSelected);
    }
}
