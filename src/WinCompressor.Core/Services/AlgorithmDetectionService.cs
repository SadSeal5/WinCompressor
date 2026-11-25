using WinCompressor.Core.Models;
using WinCompressor.Core.Native;

namespace WinCompressor.Core.Services;

/// <summary>
/// Detects the real on-disk CompactOS algorithm of a file via the documented
/// FSCTL_GET_EXTERNAL_BACKING control code (WOF_PROVIDER_FILE +
/// FILE_PROVIDER_EXTERNAL_INFO_V1). In-process, fast, no administrator rights
/// required and no compact.exe involved.
/// </summary>
public class AlgorithmDetectionService : IAlgorithmDetectionService
{
    public CompressionAlgorithm? GetCompactOsAlgorithm(string filePath)
    {
        var backing = Kernel32.GetWofExternalBacking(filePath);
        if (backing is null || backing.Value.Provider != Kernel32.WofProviderFile)
            return null;

        return MapFileProviderAlgorithm(backing.Value.FileAlgorithm);
    }

    /// <summary>
    /// Maps FILE_PROVIDER_COMPRESSION_* constants (winioctl.h) to the enum:
    /// XPRESS4K=0, LZX=1, XPRESS8K=2, XPRESS16K=3.
    /// Unknown future values map to <see cref="CompressionAlgorithm.Unknown"/>.
    /// </summary>
    public static CompressionAlgorithm MapFileProviderAlgorithm(uint fileProviderAlgorithm) =>
        fileProviderAlgorithm switch
        {
            0 => CompressionAlgorithm.Xpress4K,
            1 => CompressionAlgorithm.Lzx,
            2 => CompressionAlgorithm.Xpress8K,
            3 => CompressionAlgorithm.Xpress16K,
            _ => CompressionAlgorithm.Unknown
        };
}
