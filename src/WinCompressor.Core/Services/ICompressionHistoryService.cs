using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface ICompressionHistoryService
{
    string HistoryPath { get; }

    /// <summary>Bozuk/dosya yok durumunda boş liste döner.</summary>
    IReadOnlyList<CompressionHistoryEntry> Load();

    /// <summary>En son eklenen batch'in kayıtları; geçmiş boşsa boş liste.</summary>
    IReadOnlyList<CompressionHistoryEntry> LoadLastBatch();

    /// <summary>
    /// Kayıtları yeni bir batch olarak ekler; yeni batch kimliğini döndürür.
    /// Kapasite aşımında en eski kayıtlar baştan atılır.
    /// </summary>
    string AppendBatch(IEnumerable<CompressionHistoryEntry> entries);

    /// <summary>Geri alma tamamlandığında ilgili batch'i geçmişten düşürür.</summary>
    void RemoveBatch(string batchId);

    /// <summary>
    /// T4-M1 (inceleme turu 4): belirtilen dosyaların geçmiş girdilerini düşürür.
    /// Toplu "Sıkıştırmayı Kaldır" sonrası çağrılır — kullanıcı fiilen açtığı
    /// dosyalar için bayat geri-alma kaydı kalmasın; boşalan batch'ler doğal
    /// olarak geri alınamaz hale gelir. Filtreleme yol-karşılaştırmalıdır
    /// (büyük-küçük harf duyarsız).
    /// </summary>
    void RemoveEntries(IEnumerable<string> filePaths);

    void Clear();
}
