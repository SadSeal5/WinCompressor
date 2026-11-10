namespace WinCompressor.Core.Models;

public class FileItemInfo
{
    public required string FilePath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long Size { get; set; }
    public long AllocatedSize { get; set; }
    public CompressionAlgorithm CurrentAlgorithm { get; set; } = CompressionAlgorithm.None;
    public bool IsCompressed { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }

    public long BytesSaved => Math.Max(0, Size - AllocatedSize);
    public double CompressionRatio => Size > 0 ? (double)AllocatedSize / Size : 1.0;
    public double SavingsPercentage => Size > 0 ? Math.Max(0.0, (1.0 - ((double)AllocatedSize / Size)) * 100.0) : 0.0;
}
