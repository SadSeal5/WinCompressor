namespace WinCompressor.Core.Models;

public class CompressionResultSummary
{
    public int TotalFiles { get; init; }
    public int SuccessfulCount { get; init; }
    public int FailedCount { get; init; }
    public long TotalOriginalBytes { get; init; }
    public long TotalAllocatedBytes { get; init; }
    public long TotalBytesSaved => Math.Max(0, TotalOriginalBytes - TotalAllocatedBytes);
    public IReadOnlyList<FileItemInfo> Items { get; init; } = [];
}
