using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface IFileScannerService
{
    Task<IReadOnlyList<FileItemInfo>> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    bool IsRootDrive(string path);
}
