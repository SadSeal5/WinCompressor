using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface ICompressionRevertService
{
    /// <summary>
    /// Geçmiş kaydındaki ESKİ algoritmaya döndürür: önce tamamen açar
    /// (WOF + NTFS), ardından önceki algoritma None değilse yeniden uygular.
    /// Dosyası/erişimi olmayan kayıtlar hata olarak raporlanır.
    /// </summary>
    Task<CompressionResultSummary> RevertBatchAsync(
        IReadOnlyList<CompressionHistoryEntry> entries,
        IProgress<CompressionProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
