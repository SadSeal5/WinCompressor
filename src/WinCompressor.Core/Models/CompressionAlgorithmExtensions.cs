namespace WinCompressor.Core.Models;

/// <summary>
/// Compression algorithm domain rules.
/// </summary>
public static class CompressionAlgorithmExtensions
{
    /// <summary>
    /// CompactOS WOF algorithms (XPRESS / LZX) require administrator privileges;
    /// classic NTFS compression (LZNT1) works with standard user rights.
    /// </summary>
    public static bool RequiresAdmin(this CompressionAlgorithm algorithm) =>
        algorithm is CompressionAlgorithm.Lzx
            or CompressionAlgorithm.Xpress4K
            or CompressionAlgorithm.Xpress8K
            or CompressionAlgorithm.Xpress16K;

    /// <summary>
    /// True for CompactOS WOF algorithms (XPRESS / LZX) as opposed to classic NTFS
    /// compression. Used for filtering and column display.
    /// </summary>
    public static bool IsWofAlgorithm(this CompressionAlgorithm algorithm) =>
        algorithm.RequiresAdmin();
}
