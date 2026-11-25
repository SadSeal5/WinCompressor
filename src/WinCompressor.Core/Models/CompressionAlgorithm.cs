namespace WinCompressor.Core.Models;

public enum CompressionAlgorithm
{
    None,
    Ntfs,
    Xpress4K,
    Xpress8K,
    Xpress16K,
    Lzx,

    /// <summary>
    /// File is compressed (allocated size &lt; logical size) with an algorithm that
    /// could not be identified. Applies to WOF files whose format probe failed.
    /// </summary>
    Unknown
}
