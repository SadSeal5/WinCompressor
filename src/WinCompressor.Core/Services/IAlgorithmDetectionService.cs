using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface IAlgorithmDetectionService
{
    /// <summary>
    /// Returns the real CompactOS (WOF) compression algorithm of the file —
    /// XPRESS4K / XPRESS8K / XPRESS16K / LZX — or null when the file is not
    /// backed by WOF (plain, NTFS-compressed or sparse files).
    /// Note: NTFS-compressed files are not WOF-backed; identify those via the
    /// Compressed file attribute before calling.
    /// </summary>
    CompressionAlgorithm? GetCompactOsAlgorithm(string filePath);
}
