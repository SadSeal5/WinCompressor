using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using Xunit;

namespace WinCompressor.Tests;

public class FileScannerTests
{
    [Fact]
    public async Task Scan_ShouldIgnoreZipFile_AndOrderDescendingBySize()
    {
        using var tempDir = new TempTestDirectory();

        // Create dummy files: 2MB, 1MB, 500KB, and a .zip file
        var largeFile = tempDir.CreateFile("large.dat", 2 * 1024 * 1024);   // 2,097,152 bytes
        var mediumFile = tempDir.CreateFile("medium.dat", 1 * 1024 * 1024); // 1,048,576 bytes
        var smallFile = tempDir.CreateFile("small.dat", 500 * 1024);        // 512,000 bytes
        var zipFile = tempDir.CreateFile("archive.zip", 100 * 1024);        // 102,400 bytes

        var scanner = new FileScannerService();
        var options = new ScanOptions
        {
            RootPath = tempDir.Path,
            IncludeSubdirectories = true,
            MinFileSize = 4096
        };

        var results = await scanner.ScanAsync(options);

        // Verify scanner ignores .zip file
        Assert.DoesNotContain(results, f => f.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, results.Count);

        // Verify strictly ordered DESCENDING by size
        Assert.Equal(largeFile, results[0].FilePath);
        Assert.Equal(mediumFile, results[1].FilePath);
        Assert.Equal(smallFile, results[2].FilePath);

        Assert.True(results[0].Size > results[1].Size);
        Assert.True(results[1].Size > results[2].Size);
        Assert.Equal(2 * 1024 * 1024, results[0].Size);
        Assert.Equal(1 * 1024 * 1024, results[1].Size);
        Assert.Equal(500 * 1024, results[2].Size);
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("C:")]
    [InlineData("D:\\")]
    [InlineData("D:")]
    [InlineData("E:\\")]
    public void IsRootDrive_ShouldIdentifyDriveRootsCorrectly(string drivePath)
    {
        var scanner = new FileScannerService();
        Assert.True(scanner.IsRootDrive(drivePath));
        Assert.True(FileScannerService.CheckIsRootDrive(drivePath));
    }

    [Theory]
    [InlineData("C:\\Windows\\notepad.exe")]
    [InlineData("D:\\Folder\\subfolder\\file.txt")]
    [InlineData("temp.dat")]
    public void IsRootDrive_ShouldReturnFalseForNormalFiles(string filePath)
    {
        var scanner = new FileScannerService();
        Assert.False(scanner.IsRootDrive(filePath));
        Assert.False(FileScannerService.CheckIsRootDrive(filePath));
    }

    [Fact]
    public async Task Scan_DriveRoot_ShouldNeverReturnDriveRootAsFile()
    {
        var scanner = new FileScannerService();
        var options = new ScanOptions
        {
            RootPath = "C:\\",
            IncludeSubdirectories = false,
            MinFileSize = 4096
        };

        var results = await scanner.ScanAsync(options);

        // None of the returned items should be a drive root or directory
        foreach (var item in results)
        {
            Assert.False(scanner.IsRootDrive(item.FilePath));
            Assert.False(Directory.Exists(item.FilePath) && !File.Exists(item.FilePath));
        }
    }

    [Fact]
    public async Task Scan_ShouldHonorExcludedDirectories()
    {
        using var tempDir = new TempTestDirectory();

        tempDir.CreateFile("normal.dat", 10 * 1024);
        tempDir.CreateFile(".git/git_internal.dat", 10 * 1024);
        tempDir.CreateFile("node_modules/pkg.dat", 10 * 1024);

        var scanner = new FileScannerService();
        var options = new ScanOptions
        {
            RootPath = tempDir.Path,
            IncludeSubdirectories = true,
            MinFileSize = 4096
        };

        var results = await scanner.ScanAsync(options);

        Assert.Single(results);
        Assert.EndsWith("normal.dat", results[0].FilePath);
    }

    [Fact]
    public async Task Scan_ShouldHonorMinFileSize()
    {
        using var tempDir = new TempTestDirectory();

        tempDir.CreateFile("tiny.dat", 100); // 100 bytes < 4096 min
        tempDir.CreateFile("eligible.dat", 8192);

        var scanner = new FileScannerService();
        var options = new ScanOptions
        {
            RootPath = tempDir.Path,
            MinFileSize = 4096
        };

        var results = await scanner.ScanAsync(options);

        Assert.Single(results);
        Assert.EndsWith("eligible.dat", results[0].FilePath);
    }
}
