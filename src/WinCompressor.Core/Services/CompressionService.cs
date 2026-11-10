using System.Diagnostics;
using System.IO;
using WinCompressor.Core.Models;
using WinCompressor.Core.Native;

namespace WinCompressor.Core.Services;

public class CompressionService : ICompressionService
{
    private static readonly string CompactPath = GetCompactExecutablePath();

    private static string GetCompactExecutablePath()
    {
        var systemPath = Path.Combine(Environment.SystemDirectory, "compact.exe");
        return File.Exists(systemPath) ? systemPath : "compact.exe";
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
            var args = GetCompressArguments(algorithm, fileItem.FilePath);
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
            // First decompress any executable/WOF compression (compact.exe /u /exe "<path>")
            var (exitCode1, stdOut1, stdErr1) = await RunCompactProcessAsync(
                ["/u", "/exe", fileItem.FilePath], cancellationToken);

            // Followed by standard NTFS decompression (compact.exe /u "<path>")
            var (exitCode2, stdOut2, stdErr2) = await RunCompactProcessAsync(
                ["/u", fileItem.FilePath], cancellationToken);

            if (exitCode2 != 0 && stdOut2.Contains("[ERR]", StringComparison.OrdinalIgnoreCase))
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
        long totalSavedBytes = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : 4,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(fileList, parallelOptions, async (item, ct) =>
        {
            bool success = await DecompressFileAsync(item, ct);

            int currentProcessed = Interlocked.Increment(ref processedCount);
            if (success)
            {
                Interlocked.Increment(ref successCount);
                Interlocked.Add(ref totalOriginalBytes, item.Size);
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
                CurrentAlgorithm = CompressionAlgorithm.None
            });
        });

        return new CompressionResultSummary
        {
            TotalFiles = totalCount,
            SuccessfulCount = successCount,
            FailedCount = failedCount,
            TotalOriginalBytes = totalOriginalBytes,
            TotalAllocatedBytes = totalOriginalBytes,
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

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCompactProcessAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = CompactPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already terminated
            }
            throw;
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return (process.ExitCode, stdOut, stdErr);
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
