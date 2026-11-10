using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface ICompressionService
{
    Task<bool> CompressFileAsync(
        FileItemInfo fileItem,
        CompressionAlgorithm algorithm,
        CancellationToken cancellationToken = default);

    Task<bool> DecompressFileAsync(
        FileItemInfo fileItem,
        CancellationToken cancellationToken = default);

    Task<CompressionResultSummary> CompressFilesAsync(
        IEnumerable<FileItemInfo> files,
        CompressionAlgorithm algorithm,
        int maxDegreeOfParallelism = 4,
        IProgress<CompressionProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    Task<CompressionResultSummary> DecompressFilesAsync(
        IEnumerable<FileItemInfo> files,
        int maxDegreeOfParallelism = 4,
        IProgress<CompressionProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
