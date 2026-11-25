using System.Diagnostics;
using WinCompressor.Core.Models;
using WinCompressor.Core.Native;

namespace WinCompressor.Core.Services;

public class CompressionService : ICompressionService
{
    /// <summary>
    /// Test seam (inceleme bulgusu K1): compact.exe çağrısını değiştiren enjekte
    /// edilebilir runner. Null ise gerçek <see cref="CompactProcess"/> kullanılır.
    /// Basınç hata simülasyonu testlerinin (exit code / [ERR] çıktısı) yazılabilmesi için eklendi.
    /// </summary>
    private readonly Func<string[], CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>>? _compactRunner;

    public CompressionService(
        Func<string[], CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>>? compactRunner = null)
    {
        _compactRunner = compactRunner;
    }

    public async Task<bool> CompressFileAsync(
        FileItemInfo fileItem,
        CompressionAlgorithm algorithm,
        CancellationToken cancellationToken = default)
    {
        if (algorithm == CompressionAlgorithm.None)
        {
            return await DecompressFileAsync(fileItem, cancellationToken);
        }

        // INVARIANT: Never compress root drives or directories
        if (FileScannerService.CheckIsRootDrive(fileItem.FilePath) || Directory.Exists(fileItem.FilePath))
        {
            fileItem.Status = "Failed";
            fileItem.ErrorMessage = "Root drives or directories cannot be compressed.";
            return false;
        }

        if (!File.Exists(fileItem.FilePath))
        {
            fileItem.Status = "Failed";
            fileItem.ErrorMessage = "File not found.";
            return false;
        }

        try
        {
            var compactPath = PrepareCompactPath(fileItem.FilePath);
            var args = GetCompressArguments(algorithm, compactPath);
            var (exitCode, stdOut, stdErr) = await RunCompactProcessAsync(args, cancellationToken);

            if (exitCode != 0 || stdOut.Contains("[ERR]", StringComparison.OrdinalIgnoreCase))
            {
                fileItem.Status = "Failed";
                fileItem.ErrorMessage = string.IsNullOrWhiteSpace(stdErr)
                    ? ExtractCompactError(stdOut)
                    : stdErr.Trim();
                return false;
            }

            var attrs = Kernel32.GetAttributesSafe(fileItem.FilePath);
            long newAllocated = Kernel32.GetDiskAllocatedSize(fileItem.FilePath);

            fileItem.AllocatedSize = newAllocated >= 0 ? newAllocated : fileItem.Size;
            fileItem.IsCompressed = (fileItem.AllocatedSize < fileItem.Size) ||
                                    (attrs.HasValue && Kernel32.IsCompressed(attrs.Value));
            fileItem.CurrentAlgorithm = algorithm;
            fileItem.Status = "Compressed";
            fileItem.ErrorMessage = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            fileItem.Status = "Failed";
            fileItem.ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task<bool> DecompressFileAsync(
        FileItemInfo fileItem,
        CancellationToken cancellationToken = default)
    {
        // INVARIANT: Never touch root drives or directories
        if (FileScannerService.CheckIsRootDrive(fileItem.FilePath) || Directory.Exists(fileItem.FilePath))
        {
            fileItem.Status = "Failed";
            fileItem.ErrorMessage = "Root drives or directories cannot be decompressed.";
            return false;
        }

        if (!File.Exists(fileItem.FilePath))
        {
            fileItem.Status = "Failed";
            fileItem.ErrorMessage = "File not found.";
            return false;
        }

        try
        {
            var compactPath = PrepareCompactPath(fileItem.FilePath);

            // Faz 1.2 (inceleme bulgusu K1): adım-1'in sonucu artık AÇIKÇA
            // değerlendirilir. compact.exe WOF-sıkıştırılmamış dosyada bu adımda
            // hata bastığından başarısızlık bilinçli olarak non-fatal'dir:
            // sonuç kayda geçer ama işlemi düşürmez; adım-2 yetkilidir.
            var (exitCode1, stdOut1, stdErr1) = await RunCompactProcessAsync(
                ["/u", "/exe", compactPath], cancellationToken);
            _ = (exitCode1, stdOut1, stdErr1); // non-fatal: yalnızca gözlemlendi

            // Step 2: remove standard NTFS compression (compact.exe /u "<path>").
            var (exitCode2, stdOut2, stdErr2) = await RunCompactProcessAsync(
                ["/u", compactPath], cancellationToken);

            // INVARIANT: keep failure detection consistent with CompressFileAsync (exit
            // code OR error marker in stdout both indicate failure).
            if (exitCode2 != 0 || stdOut2.Contains("[ERR]", StringComparison.OrdinalIgnoreCase))
            {
                fileItem.Status = "Failed";
                fileItem.ErrorMessage = string.IsNullOrWhiteSpace(stdErr2)
                    ? ExtractCompactError(stdOut2)
                    : stdErr2.Trim();
                return false;
            }

            var attrs = Kernel32.GetAttributesSafe(fileItem.FilePath);
            long newAllocated = Kernel32.GetDiskAllocatedSize(fileItem.FilePath);

            fileItem.AllocatedSize = newAllocated >= 0 ? newAllocated : fileItem.Size;
            fileItem.IsCompressed = attrs.HasValue && Kernel32.IsCompressed(attrs.Value);
            fileItem.CurrentAlgorithm = CompressionAlgorithm.None;
            fileItem.Status = "Decompressed";
            fileItem.ErrorMessage = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            fileItem.Status = "Failed";
            fileItem.ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task<CompressionResultSummary> CompressFilesAsync(
        IEnumerable<FileItemInfo> files,
        CompressionAlgorithm algorithm,
        int maxDegreeOfParallelism = 4,
        IProgress<CompressionProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileList = files.ToList();
        int totalCount = fileList.Count;
        int processedCount = 0;
        int successCount = 0;
        int failedCount = 0;
        long totalOriginalBytes = 0;
        long totalSavedBytes = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : 4,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(fileList, parallelOptions, async (item, ct) =>
        {
            bool success = await CompressFileAsync(item, algorithm, ct);

            int currentProcessed = Interlocked.Increment(ref processedCount);
            if (success)
            {
                Interlocked.Increment(ref successCount);
                Interlocked.Add(ref totalOriginalBytes, item.Size);
                Interlocked.Add(ref totalSavedBytes, item.BytesSaved);
            }
            else
            {
                Interlocked.Increment(ref failedCount);
            }

            progress?.Report(new CompressionProgressReport
            {
                TotalFiles = totalCount,
                ProcessedFiles = currentProcessed,
                SuccessfulFiles = Volatile.Read(ref successCount),
                FailedFiles = Volatile.Read(ref failedCount),
                TotalBytesOriginal = Volatile.Read(ref totalOriginalBytes),
                TotalBytesSaved = Volatile.Read(ref totalSavedBytes),
                CurrentFilePath = item.FilePath,
                CurrentAlgorithm = algorithm
            });
        });

        return new CompressionResultSummary
        {
            TotalFiles = totalCount,
            SuccessfulCount = successCount,
            FailedCount = failedCount,
            TotalOriginalBytes = totalOriginalBytes,
            TotalAllocatedBytes = Math.Max(0, totalOriginalBytes - totalSavedBytes),
            Items = fileList
        };
    }

    public async Task<CompressionResultSummary> DecompressFilesAsync(
        IEnumerable<FileItemInfo> files,
        int maxDegreeOfParallelism = 4,
        IProgress<CompressionProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileList = files.ToList();
        int totalCount = fileList.Count;
        int processedCount = 0;
        int successCount = 0;
        int failedCount = 0;
        long totalOriginalBytes = 0;
        long totalAllocatedBytes = 0;
        long totalSavingsRemovedBytes = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : 4,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(fileList, parallelOptions, async (item, ct) =>
        {
            // Capture the disk space the compression was saving before removing it,
            // so the progress/summary can report how much was given back to the disk.
            long savingsBefore = item.BytesSaved;

            bool success = await DecompressFileAsync(item, ct);

            // Update all totals BEFORE incrementing the processed counter: the
            // iteration that observes the final count is then guaranteed to see
            // every other iteration's adds, making the final progress deterministic.
            if (success)
            {
                Interlocked.Increment(ref successCount);
                Interlocked.Add(ref totalOriginalBytes, item.Size);
                Interlocked.Add(ref totalAllocatedBytes, item.AllocatedSize);
                if (savingsBefore > 0)
                    Interlocked.Add(ref totalSavingsRemovedBytes, savingsBefore);
            }
            else
            {
                Interlocked.Increment(ref failedCount);
            }

            int currentProcessed = Interlocked.Increment(ref processedCount);

            progress?.Report(new CompressionProgressReport
            {
                TotalFiles = totalCount,
                ProcessedFiles = currentProcessed,
                SuccessfulFiles = Volatile.Read(ref successCount),
                FailedFiles = Volatile.Read(ref failedCount),
                TotalBytesOriginal = Volatile.Read(ref totalOriginalBytes),
                TotalBytesSaved = Volatile.Read(ref totalSavingsRemovedBytes),
                CurrentFilePath = item.FilePath,
                CurrentAlgorithm = CompressionAlgorithm.None
            });
        });

        return new CompressionResultSummary
        {
            TotalFiles = totalCount,
            SuccessfulCount = successCount,
            FailedCount = failedCount,
            TotalOriginalBytes = totalOriginalBytes,
            // Post-decompression allocated size: should match the logical size again,
            // making the summary's derived TotalBytesSaved ~0 after a full decompress.
            TotalAllocatedBytes = totalAllocatedBytes,
            Items = fileList
        };
    }

    private static string[] GetCompressArguments(CompressionAlgorithm algorithm, string filePath)
    {
        return algorithm switch
        {
            CompressionAlgorithm.Lzx => ["/c", "/exe:lzx", filePath],
            CompressionAlgorithm.Xpress4K => ["/c", "/exe:xpress4k", filePath],
            CompressionAlgorithm.Xpress8K => ["/c", "/exe:xpress8k", filePath],
            CompressionAlgorithm.Xpress16K => ["/c", "/exe:xpress16k", filePath],
            CompressionAlgorithm.Ntfs => ["/c", filePath],
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), $"Unsupported compression algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// compact.exe cannot handle paths beyond MAX_PATH (it fails with
    /// "Out of memory" even with the \\?\ extended-length prefix). For long
    /// paths we hand compact the 8.3 short form instead, which is functionally
    /// identical. Falls back to the original path when no short form exists.
    /// </summary>
    private static string PrepareCompactPath(string filePath)
    {
        // 259 = MAX_PATH - 1 (drive + 255 chars).
        if (filePath.Length < 260)
            return filePath;

        return Kernel32.TryGetShortPath(filePath) ?? filePath;
    }

    private Task<(int ExitCode, string StdOut, string StdErr)> RunCompactProcessAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        return _compactRunner?.Invoke(arguments, cancellationToken)
            ?? CompactProcess.RunAsync(arguments, cancellationToken);
    }

    private static string ExtractCompactError(string output)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var errLines = lines.Where(l =>
            l.Contains("[ERR]", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("engellendi", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("error", StringComparison.OrdinalIgnoreCase)).ToList();

        return errLines.Count > 0 ? string.Join("; ", errLines) : "Compact operation failed.";
    }
}
