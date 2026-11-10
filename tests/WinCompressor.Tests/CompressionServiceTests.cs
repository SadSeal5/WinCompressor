using System.Security.Cryptography;
using WinCompressor.Core.Models;
using WinCompressor.Core.Native;
using WinCompressor.Core.Services;
using Xunit;

namespace WinCompressor.Tests;

public class CompressionServiceTests
{
    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    [Fact]
    public async Task CompressAndDecompress_Ntfs_ShouldBeLosslessAndReduceSize()
    {
        using var tempDir = new TempTestDirectory();

        // 1. Creates a compressible 2MB text file in temp folder
        const int fileSize = 2 * 1024 * 1024; // 2MB
        var filePath = tempDir.CreateFile("compressible.txt", fileSize, fillByte: (byte)'Z');

        // 2. Calculates initial SHA256 checksum
        var initialChecksum = ComputeSha256(filePath);
        var initialAllocated = Kernel32.GetDiskAllocatedSize(filePath);
        Assert.True(initialAllocated >= fileSize, "Initial allocated size should be >= logical size.");

        var fileItem = new FileItemInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Extension = Path.GetExtension(filePath),
            Size = fileSize,
            AllocatedSize = initialAllocated
        };

        var service = new CompressionService();

        // 3. Compresses with Ntfs algorithm
        var compressSuccess = await service.CompressFileAsync(fileItem, CompressionAlgorithm.Ntfs);
        Assert.True(compressSuccess, $"Compression failed: {fileItem.ErrorMessage}");
        Assert.True(fileItem.IsCompressed);
        Assert.Equal(CompressionAlgorithm.Ntfs, fileItem.CurrentAlgorithm);
        Assert.Equal("Compressed", fileItem.Status);

        // 4. Verifies GetDiskAllocatedSize is significantly smaller than logical size
        var compressedAllocated = Kernel32.GetDiskAllocatedSize(filePath);
        Assert.True(compressedAllocated < fileSize / 2,
            $"Compressed allocated size ({compressedAllocated}) should be significantly smaller than logical size ({fileSize}).");
        Assert.Equal(compressedAllocated, fileItem.AllocatedSize);

        // Verify SHA256 checksum is lossless while compressed
        var compressedChecksum = ComputeSha256(filePath);
        Assert.Equal(initialChecksum, compressedChecksum);

        // 5. Uncompresses file
        var decompressSuccess = await service.DecompressFileAsync(fileItem);
        Assert.True(decompressSuccess, $"Decompression failed: {fileItem.ErrorMessage}");
        Assert.False(fileItem.IsCompressed);
        Assert.Equal(CompressionAlgorithm.None, fileItem.CurrentAlgorithm);
        Assert.Equal("Decompressed", fileItem.Status);

        // 6. Verifies disk size returns to normal and SHA256 matches initial checksum exactly
        var decompressedAllocated = Kernel32.GetDiskAllocatedSize(filePath);
        Assert.True(decompressedAllocated >= fileSize,
            $"Decompressed allocated size ({decompressedAllocated}) should return to normal (>= {fileSize}).");

        var finalChecksum = ComputeSha256(filePath);
        Assert.Equal(initialChecksum, finalChecksum);
    }

    [Fact]
    public async Task Compress_RootDrive_ShouldFailSafelyWithoutModifyingDrive()
    {
        var service = new CompressionService();
        var rootItem = new FileItemInfo
        {
            FilePath = "C:\\",
            FileName = "C:\\",
            Size = 0,
            AllocatedSize = 0
        };

        var success = await service.CompressFileAsync(rootItem, CompressionAlgorithm.Ntfs);

        Assert.False(success);
        Assert.Equal("Failed", rootItem.Status);
        Assert.Contains("Root drive", rootItem.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compress_NonExistentFile_ShouldRecordErrorGracefully()
    {
        var service = new CompressionService();
        var missingItem = new FileItemInfo
        {
            FilePath = "C:\\NonExistent_Dir_12345\\missing_file.xyz",
            FileName = "missing_file.xyz",
            Size = 1000,
            AllocatedSize = 1000
        };

        var success = await service.CompressFileAsync(missingItem, CompressionAlgorithm.Ntfs);

        Assert.False(success);
        Assert.Equal("Failed", missingItem.Status);
        Assert.NotNull(missingItem.ErrorMessage);
    }

    [Fact]
    public async Task BatchCompressAndDecompress_ShouldProcessInParallelWithProgress()
    {
        using var tempDir = new TempTestDirectory();

        var files = new List<FileItemInfo>();
        for (int i = 0; i < 4; i++)
        {
            var p = tempDir.CreateFile($"batch_{i}.txt", 256 * 1024, fillByte: (byte)('A' + i));
            files.Add(new FileItemInfo
            {
                FilePath = p,
                FileName = Path.GetFileName(p),
                Size = 256 * 1024,
                AllocatedSize = Kernel32.GetDiskAllocatedSize(p)
            });
        }

        var service = new CompressionService();
        var progressReports = new List<CompressionProgressReport>();
        var progress = new Progress<CompressionProgressReport>(r => progressReports.Add(r));

        var summary = await service.CompressFilesAsync(files, CompressionAlgorithm.Ntfs, maxDegreeOfParallelism: 2, progress: progress);

        Assert.Equal(4, summary.TotalFiles);
        Assert.Equal(4, summary.SuccessfulCount);
        Assert.Equal(0, summary.FailedCount);
        Assert.True(summary.TotalBytesSaved > 0);

        // Decompress all
        var decompressSummary = await service.DecompressFilesAsync(files, maxDegreeOfParallelism: 2);
        Assert.Equal(4, decompressSummary.TotalFiles);
        Assert.Equal(4, decompressSummary.SuccessfulCount);
    }

    [Fact]
    public async Task TransparentCompression_DocumentFile_ShouldBeDirectlyReadableAndUsableWhileDiskSizeReduced()
    {
        using var tempDir = new TempTestDirectory();

        // 1. Create a structured document file (formatted text document)
        var docPath = Path.Combine(tempDir.Path, "MonthlyFinancialReport.txt");
        var docBuilder = new System.Text.StringBuilder();
        docBuilder.AppendLine("=================================================");
        docBuilder.AppendLine("       MONTHLY FINANCIAL & AUDIT REPORT          ");
        docBuilder.AppendLine("=================================================");
        docBuilder.AppendLine();

        // Generate repetitive structured document sections (compressible text)
        for (int i = 1; i <= 2000; i++)
        {
            docBuilder.AppendLine($"[SECTION {i:D4}] Transaction Record");
            docBuilder.AppendLine($"ID: TXN-{i:D8} | Account: ACCT-9876543210 | Type: Credit");
            docBuilder.AppendLine($"Description: Automated recurrent settlement batch for regional office #{i % 25}");
            docBuilder.AppendLine($"Status: Verified | Hash: {new string((char)('A' + (i % 26)), 32)}");
            docBuilder.AppendLine(new string('-', 50));
        }

        var originalContent = docBuilder.ToString();
        await File.WriteAllTextAsync(docPath, originalContent, System.Text.Encoding.UTF8);

        var fileInfo = new FileInfo(docPath);
        long originalLogicalSize = fileInfo.Length;
        string originalChecksum = ComputeSha256(docPath);

        var fileItem = new FileItemInfo
        {
            FilePath = docPath,
            FileName = fileInfo.Name,
            Extension = fileInfo.Extension,
            Size = originalLogicalSize,
            AllocatedSize = Kernel32.GetDiskAllocatedSize(docPath)
        };

        var service = new CompressionService();

        // 2. Compress with Windows filesystem compression (NTFS)
        var compressSuccess = await service.CompressFileAsync(fileItem, CompressionAlgorithm.Ntfs);
        Assert.True(compressSuccess, $"Compression failed: {fileItem.ErrorMessage}");
        Assert.True(fileItem.IsCompressed);

        // 3. Verify physical disk allocated size is significantly reduced
        long compressedAllocatedSize = Kernel32.GetDiskAllocatedSize(docPath);
        Assert.True(compressedAllocatedSize < originalLogicalSize / 2,
            $"Allocated size ({compressedAllocatedSize}) must be significantly smaller than logical size ({originalLogicalSize})");

        // 4. TRANSPARENT ACCESS VERIFICATION:
        // Normal programs (without any decompression step or external utility)
        // must be able to directly open, stream, and read the file completely and flawlessly.
        using (var stream = File.OpenRead(docPath))
        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
        {
            var readContent = await reader.ReadToEndAsync();
            Assert.Equal(originalContent.Length, readContent.Length);
            Assert.Equal(originalContent, readContent);
        }

        // Verify SHA256 checksum when opened directly matches original before compression
        string directlyReadChecksum = ComputeSha256(docPath);
        Assert.Equal(originalChecksum, directlyReadChecksum);

        // 5. Verify the file is active and directly writable/appendable
        const string appendNote = "\n[AUDIT NOTE: Directly appended to transparently compressed file]";
        await File.AppendAllTextAsync(docPath, appendNote, System.Text.Encoding.UTF8);

        var updatedContent = await File.ReadAllTextAsync(docPath, System.Text.Encoding.UTF8);
        Assert.EndsWith(appendNote, updatedContent);
        Assert.StartsWith("=================================================", updatedContent);
    }
}

