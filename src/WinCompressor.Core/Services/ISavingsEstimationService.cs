using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface ISavingsEstimationService
{
    /// <summary>
    /// Estimates the disk space that compressing the file with the given algorithm
    /// would save, WITHOUT touching the file. Heuristic dry-run: samples chunks of
    /// the file, compresses them in-memory (Deflate) and scales the result by an
    /// algorithm-specific factor. The estimate is indicative, not a guarantee.
    /// </summary>
    Task<long> EstimateSavedBytesAsync(
        string filePath,
        long fileSize,
        CompressionAlgorithm algorithm,
        CancellationToken cancellationToken = default);
}
