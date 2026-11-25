namespace WinCompressor.Core.Models;

/// <summary>
/// Geri alma güvenlik ağı (Faz 5.4): bir sıkıştırma işleminde dosyanın
/// ESKİ algoritması kaydedilir; kullanıcı tek tuşla eski haline döndürebilir.
/// </summary>
public class CompressionHistoryEntry
{
    /// <summary>Hedef dosyanın tam yolu.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>İşlem ÖNCESİ algoritma (None / Ntfs / Lzx / Xpress*).</summary>
    public string PreviousAlgorithm { get; set; } = nameof(CompressionAlgorithm.None);

    /// <summary>İşlem ile AYARLANAN algoritma.</summary>
    public string NewAlgorithm { get; set; } = string.Empty;

    /// <summary>Aynı toplu işlemi gruplayan kimlik (tek tuşla geri alma birimi).</summary>
    public string BatchId { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
