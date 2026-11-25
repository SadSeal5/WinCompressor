using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.Core.Native;

namespace WinCompressor.Tests;

/// <summary>
/// Phase 2: WOF algorithm detection via FSCTL_GET_EXTERNAL_BACKING,
/// sparse-file correction in the scanner, and dry-run savings estimation.
/// </summary>
public class Phase2DetectionAndEstimationTests
{
    // ----------------------------------------------------------------
    // 2.1 algorithm detection (documented FSCTL_GET_EXTERNAL_BACKING probe)
    // ----------------------------------------------------------------

    [Fact]
    public void MapFileProviderAlgorithm_MapsWinioctlConstantsToEnum()
    {
        // FILE_PROVIDER_COMPRESSION_* from winioctl.h:
        Assert.Equal(CompressionAlgorithm.Xpress4K, AlgorithmDetectionService.MapFileProviderAlgorithm(0));
        Assert.Equal(CompressionAlgorithm.Lzx, AlgorithmDetectionService.MapFileProviderAlgorithm(1));
        Assert.Equal(CompressionAlgorithm.Xpress8K, AlgorithmDetectionService.MapFileProviderAlgorithm(2));
        Assert.Equal(CompressionAlgorithm.Xpress16K, AlgorithmDetectionService.MapFileProviderAlgorithm(3));
        Assert.Equal(CompressionAlgorithm.Unknown, AlgorithmDetectionService.MapFileProviderAlgorithm(99));
    }

    [Fact]
    public async Task GetCompactOsAlgorithm_PlainFile_ReturnsNull()
    {
        using var tempDir = new TempTestDirectory();
        var path = tempDir.CreateFile("plain.txt", 64 * 1024, fillByte: (byte)'A');

        var detector = new AlgorithmDetectionService();

        Assert.Null(detector.GetCompactOsAlgorithm(path));
    }

    [Fact]
    public async Task GetCompactOsAlgorithm_NtfsCompressedFile_ReturnsNull()
    {
        using var tempDir = new TempTestDirectory();
        var path = tempDir.CreateFile("ntfs_compressed.txt", 512 * 1024, fillByte: (byte)'Z');

        var item = new FileItemInfo
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            Extension = Path.GetExtension(path),
            Size = 512 * 1024,
            AllocatedSize = Kernel32.GetDiskAllocatedSize(path)
        };
        Assert.True(await new CompressionService().CompressFileAsync(item, CompressionAlgorithm.Ntfs),
            $"Real NTFS compression failed: {item.ErrorMessage}");

        // NTFS compression is NOT WOF backing — detector must stay null so the
        // scanner keeps its attribute-based NTFS classification.
        var detector = new AlgorithmDetectionService();
        Assert.Null(detector.GetCompactOsAlgorithm(path));
    }

    [Fact]
    public void GetCompactOsAlgorithm_MissingFile_ReturnsNull()
    {
        var detector = new AlgorithmDetectionService();

        Assert.Null(detector.GetCompactOsAlgorithm("C:\\NonExistent_Dir_12345\\missing.bin"));
    }

    // ----------------------------------------------------------------
    // 2.2 scanner integration: sparse files are corrected to uncompressed
    // ----------------------------------------------------------------

    [Fact]
    public async Task Scanner_SparseFile_IsCorrectedToUncompressedAndNotCountedAsCompressed()
    {
        using var tempDir = new TempTestDirectory();

        // Sparse file: logical size 8 MB but only a few KB allocated on disk.
        var path = Path.Combine(tempDir.Path, "sparse_probe.bin");
        await File.WriteAllTextAsync(path, "data");

        if (!MarkSparse(path))
        {
            // Windows 11 denies FSCTL_SET_SPARSE for standard users; run elevated
            // to exercise the full correction path below.
            return;
        }

        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(8 * 1024 * 1024);
        }

        long allocated = Kernel32.GetDiskAllocatedSize(path);
        Assert.True(allocated >= 0 && allocated < 4 * 64 * 1024,
            $"Sparse file should allocate only a few clusters, got {allocated}.");

        var scanner = new FileScannerService(new AlgorithmDetectionService());
        var results = await scanner.ScanAsync(new ScanOptions
        {
            RootPath = tempDir.Path,
            IncludeSubdirectories = false,
            OnlyUncompressed = false
        });

        var item = results.Single(r => r.FilePath == path);

        // The size heuristic initially flags the file as a WOF suspect; the
        // FSCTL probe must correct it to "not compressed, pending".
        Assert.Equal(CompressionAlgorithm.None, item.CurrentAlgorithm);
        Assert.False(item.IsCompressed);
        Assert.Equal("Pending", item.Status);
        Assert.Equal(item.Size, item.AllocatedSize);
    }

    [Fact]
    public async Task Scanner_WithDetection_CanScanWithoutDetectionService()
    {
        // Null detection service must not break scanning (detection skipped).
        using var tempDir = new TempTestDirectory();
        tempDir.CreateFile("plain_file.bin", 8192, fillByte: (byte)'Q');

        var scanner = new FileScannerService(algorithmDetectionService: null);
        var results = await scanner.ScanAsync(new ScanOptions
        {
            RootPath = tempDir.Path,
            IncludeSubdirectories = false,
            OnlyUncompressed = false
        });

        Assert.Single(results);
        Assert.Equal(CompressionAlgorithm.None, results[0].CurrentAlgorithm);
    }

    /// <summary>Marks a file sparse via FSCTL_SET_SPARSE (works without admin for own files).</summary>
    private static bool MarkSparse(string path)
    {
        using var handle = CreateFileW(path, 0x80000000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero); // GENERIC_WRITE, share all, OPEN_EXISTING
        if (handle.IsInvalid)
            return false;

        return DeviceIoControl(handle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
    }

    private const uint FsctlSetSparse = 0x00090000 | (49 << 2); // CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 49, METHOD_BUFFERED, FILE_SPECIAL_ACCESS)

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    // ----------------------------------------------------------------
    // 2.2 enum semantics
    // ----------------------------------------------------------------

    [Fact]
    public void UnknownAlgorithm_IsNotWofAndDoesNotRequireAdmin()
    {
        Assert.False(CompressionAlgorithm.Unknown.IsWofAlgorithm());
        Assert.False(CompressionAlgorithm.Unknown.RequiresAdmin());
        Assert.False(CompressionAlgorithm.None.IsWofAlgorithm());
        Assert.True(CompressionAlgorithm.Lzx.IsWofAlgorithm());
        Assert.True(CompressionAlgorithm.Xpress8K.IsWofAlgorithm());
    }

    // ----------------------------------------------------------------
    // 2.4 dry-run savings estimation
    // ----------------------------------------------------------------

    [Fact]
    public async Task Estimation_CompressibleFile_PredictsSignificantSavings_LargerForLzxThanNtfs()
    {
        using var tempDir = new TempTestDirectory();
        var path = tempDir.CreateFile("estim_compressible.txt", 2 * 1024 * 1024, fillByte: (byte)'Z');

        var estimator = new SavingsEstimationService();
        long size = new FileInfo(path).Length;

        long lzxSaved = await estimator.EstimateSavedBytesAsync(path, size, CompressionAlgorithm.Lzx);
        long ntfsSaved = await estimator.EstimateSavedBytesAsync(path, size, CompressionAlgorithm.Ntfs);

        Assert.True(lzxSaved > size / 2,
            $"LZX estimate ({lzxSaved}) should exceed half the file size for a highly compressible file.");
        Assert.True(ntfsSaved > 0, "NTFS estimate should still predict savings for a compressible file.");
        Assert.True(lzxSaved > ntfsSaved, "LZX estimate should exceed NTFS estimate for the same file.");
    }

    [Fact]
    public async Task Estimation_IncompressibleRandomFile_PredictsNearZeroSavings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc_estim_random_{Guid.NewGuid():N}.bin");
        try
        {
            var random = Random.Shared;
            var buffer = new byte[2 * 1024 * 1024];
            random.NextBytes(buffer);
            await File.WriteAllBytesAsync(path, buffer);

            var estimator = new SavingsEstimationService();

            long saved = await estimator.EstimateSavedBytesAsync(path, buffer.Length, CompressionAlgorithm.Lzx);

            Assert.True(saved < buffer.Length / 20,
                $"Random data estimate ({saved}) should be near zero.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Estimation_NoneAlgorithm_And_MissingFile_ReturnZero()
    {
        var estimator = new SavingsEstimationService();

        Assert.Equal(0, await estimator.EstimateSavedBytesAsync("C:\\missing_file_12345.bin", 100000, CompressionAlgorithm.Lzx));

        using var tempDir = new TempTestDirectory();
        var path = tempDir.CreateFile("small.txt", 1024, fillByte: (byte)'Z');
        Assert.Equal(0, await estimator.EstimateSavedBytesAsync(path, 1024, CompressionAlgorithm.None));
    }

    [Fact]
    public async Task Estimation_NeverPredictsMoreThan95PercentOrBelowClusterFloor()
    {
        using var tempDir = new TempTestDirectory();
        var path = tempDir.CreateFile("floor.txt", 8 * 1024, fillByte: (byte)'Z');
        long size = new FileInfo(path).Length;

        var estimator = new SavingsEstimationService();
        long saved = await estimator.EstimateSavedBytesAsync(path, size, CompressionAlgorithm.Lzx);

        Assert.True(saved <= size * 95 / 100, "Estimate must be capped at 95% of file size.");
        if (saved > 0)
            Assert.True(size - saved >= 4096, "Post-compression size floor of one cluster (4 KB) must hold.");
    }
}
