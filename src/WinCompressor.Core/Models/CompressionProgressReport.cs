namespace WinCompressor.Core.Models;

public class CompressionProgressReport
{
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public int SuccessfulFiles { get; init; }
    public int FailedFiles { get; init; }
    public long TotalBytesOriginal { get; init; }
    public long TotalBytesSaved { get; init; }
    public string CurrentFilePath { get; init; } = string.Empty;
    public CompressionAlgorithm CurrentAlgorithm { get; init; }
    public double PercentComplete => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100.0 : 0.0;
}
