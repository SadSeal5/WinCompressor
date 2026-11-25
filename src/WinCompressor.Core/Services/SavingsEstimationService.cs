using System.IO.Compression;
using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

/// <summary>
/// Dry-run savings estimator. Samples a few chunks of the file, compresses them
/// in-memory with Deflate, and scales the observed ratio by an algorithm factor:
/// LZX compresses at least as well as Deflate, XPRESS slightly worse, NTFS LZNT1
/// noticeably worse.
/// </summary>
public class SavingsEstimationService : ISavingsEstimationService
{
    private const int ChunkSize = 512 * 1024;
    private const long MinFileSizeForEstimation = 4096;

    /// <summary>Never predict an allocated size below one cluster.</summary>
    private const long MinAllocatedBytes = 4096;

    /// <summary>Never claim more than 95% savings.</summary>
    private const double MaxSavingsRatio = 0.95;

    private static double AlgorithmFactor(CompressionAlgorithm algorithm) => algorithm switch
    {
        CompressionAlgorithm.Lzx => 1.00,
        CompressionAlgorithm.Xpress16K => 0.85,
        CompressionAlgorithm.Xpress8K => 0.80,
        CompressionAlgorithm.Xpress4K => 0.75,
        CompressionAlgorithm.Ntfs => 0.60,
        CompressionAlgorithm.Unknown => 0.85,
        _ => 0.0,
    };

    public async Task<long> EstimateSavedBytesAsync(
        string filePath,
        long fileSize,
        CompressionAlgorithm algorithm,
        CancellationToken cancellationToken = default)
    {
        if (fileSize < MinFileSizeForEstimation || !File.Exists(filePath))
            return 0;

        double factor = AlgorithmFactor(algorithm);
        if (factor <= 0.0)
            return 0;

        try
        {
            double ratio = await SampleCompressionRatioAsync(filePath, fileSize, cancellationToken);

            long saved = (long)(fileSize * (1.0 - ratio) * factor);
            if (saved <= 0)
                return 0;

            saved = Math.Min(saved, (long)(fileSize * MaxSavingsRatio));

            // Keep a realistic floor for the post-compression on-disk size.
            if (fileSize - saved < MinAllocatedBytes)
                saved = Math.Max(0, fileSize - MinAllocatedBytes);

            return saved;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Unreadable/locked file: no estimate.
            return 0;
        }
    }

    /// <summary>
    /// Reads up to three 512 KB samples (start / middle / end) and returns the
    /// Deflate compressed/input ratio across all samples, clamped to [0.02, 1.0].
    /// Small files are sampled entirely via the single start chunk.
    /// </summary>
    private static async Task<double> SampleCompressionRatioAsync(string path, long size, CancellationToken ct)
    {
        long[] offsets = size > ChunkSize * 3
            ? [0, (size - ChunkSize) / 2, size - ChunkSize]
            : [0];

        long totalRead = 0;
        long totalCompressed = 0;
        var buffer = new byte[ChunkSize];

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            ChunkSize, useAsync: true);

        foreach (var offset in offsets)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            int read = await stream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct);
            if (read <= 0)
                break;

            totalRead += read;
            totalCompressed += CompressSample(buffer, read);
        }

        if (totalRead == 0)
            return 1.0;

        double ratio = (double)totalCompressed / totalRead;
        return Math.Clamp(ratio, 0.02, 1.0);
    }

    private static long CompressSample(byte[] data, int length)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, length);
        }
        return output.Length;
    }
}
