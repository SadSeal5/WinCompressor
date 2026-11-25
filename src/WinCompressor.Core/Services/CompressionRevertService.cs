using System.IO;
using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

/// <summary>
/// Geri alma güvenlik ağı (Faz 5.4): sıkıştırma öncesi kaydedilen algoritmaya
/// döndürür. Adım 1: mevcut sıkıştırmayı tamamen kaldır (WOF + NTFS);
/// adım 2: önceki algoritma None değilse yeniden uygula.
/// </summary>
public class CompressionRevertService : ICompressionRevertService
{
    private readonly ICompressionService _compression;

    public CompressionRevertService(ICompressionService compression)
    {
        _compression = compression ?? throw new ArgumentNullException(nameof(compression));
    }

    public async Task<CompressionResultSummary> RevertBatchAsync(
        IReadOnlyList<CompressionHistoryEntry> entries,
        IProgress<CompressionProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        int successCount = 0;
        int failedCount = 0;
        long totalOriginalBytes = 0;
        var items = new List<FileItemInfo>();

        for (int i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[i];
            var item = BuildItem(entry.FilePath);
            items.Add(item);

            bool success = false;

            // K10 (inceleme bulgusu): "Size > 0" mevcut-ama-0-bayt dosyayı da
            // "File not found" sayıyordu; varlık kontrolü dosya boyutundan bağımsız.
            bool fileExists;
            try
            {
                fileExists = File.Exists(entry.FilePath);
            }
            catch
            {
                fileExists = false;
            }

            if (fileExists)
            {
                success = await _compression.DecompressFileAsync(item, cancellationToken);

                // T2-K4 (inceleme turu 2): önceki algoritma "Unknown" ise yeniden
                // uygulanamaz (CompactProcess ArgumentException fırlatırır) ve
                // dosya her denemede "Failed" olurdu. Unknown, "öncedeki algoritma
                // bilinmiyor" demektir: açılmış hâli doğru sonuç sayılır.
                if (success &&
                    Enum.TryParse<CompressionAlgorithm>(entry.PreviousAlgorithm, out var previous) &&
                    previous != CompressionAlgorithm.None &&
                    previous != CompressionAlgorithm.Unknown)
                {
                    // Önceki durumuna döndür: eski algoritmayı yeniden uygula.
                    success = await _compression.CompressFileAsync(item, previous, cancellationToken);
                }
            }
            else
            {
                item.Status = "Failed";
                item.ErrorMessage = "File not found.";
            }

            if (success)
            {
                successCount++;
                totalOriginalBytes += item.Size;
            }
            else
            {
                failedCount++;
            }

            progress?.Report(new CompressionProgressReport
            {
                TotalFiles = entries.Count,
                ProcessedFiles = i + 1,
                SuccessfulFiles = successCount,
                FailedFiles = failedCount,
                TotalBytesOriginal = totalOriginalBytes,
                TotalBytesSaved = 0,
                CurrentFilePath = entry.FilePath,
                CurrentAlgorithm = CompressionAlgorithm.None
            });
        }

        return new CompressionResultSummary
        {
            TotalFiles = entries.Count,
            SuccessfulCount = successCount,
            FailedCount = failedCount,
            TotalOriginalBytes = totalOriginalBytes,
            TotalAllocatedBytes = items.Where(i => i.Status == "Compressed" || i.Status == "Decompressed").Sum(i => i.AllocatedSize),
            Items = items
        };
    }

    /// <summary>Geçmiş kaydından yola göre dosya modeli üretir; dosya yoksa işaretlenmiş boş model.</summary>
    private static FileItemInfo BuildItem(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            return new FileItemInfo
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                Extension = fileInfo.Extension,
                Size = fileInfo.Length,
                AllocatedSize = fileInfo.Length
            };
        }
        catch
        {
            return new FileItemInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };
        }
    }
}
