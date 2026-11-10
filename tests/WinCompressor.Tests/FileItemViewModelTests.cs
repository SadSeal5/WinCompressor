using WinCompressor.Core.Models;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

public class FileItemViewModelTests
{
    [Fact]
    public void FileItemViewModel_WrapsModelProperly()
    {
        var model = new FileItemInfo
        {
            FilePath = "C:\\test\\file.txt",
            FileName = "file.txt",
            Extension = ".txt",
            Size = 2000,
            AllocatedSize = 1000,
            CurrentAlgorithm = CompressionAlgorithm.Ntfs,
            IsCompressed = true,
            Status = "Compressed"
        };

        bool selectionNotified = false;
        var vm = new FileItemViewModel(model, item => selectionNotified = true);

        Assert.Equal("C:\\test\\file.txt", vm.FilePath);
        Assert.Equal("file.txt", vm.FileName);
        Assert.Equal(".txt", vm.Extension);
        Assert.Equal(2000, vm.Size);
        Assert.Equal(1000, vm.AllocatedSize);
        Assert.Equal(1000, vm.BytesSaved);
        Assert.Equal(50.0, vm.SavingsPercentage);
        Assert.Equal("Compressed", vm.Status);
        Assert.True(vm.IsCompressed);
        Assert.Equal(CompressionAlgorithm.Ntfs, vm.CurrentAlgorithm);

        vm.IsSelected = true;
        Assert.True(selectionNotified);
        Assert.True(vm.IsSelected);
    }
}
