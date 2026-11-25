using System.Text.Json;
using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

/// <summary>
/// JSON dosyasına dayalı geri alma geçmişi (%AppData%\WinCompressor\history.json).
/// Kapasite sınırlı (varsayon 500 kayıt), atomik yazım, bozuk dosya → boş geçmiş.
/// </summary>
public class JsonCompressionHistoryService : ICompressionHistoryService
{
    private const int DefaultCapacity = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyPath;
    private readonly int _capacity;

    public JsonCompressionHistoryService(string? historyFolder = null, int capacity = DefaultCapacity)
    {
        var folder = historyFolder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinCompressor");

        Directory.CreateDirectory(folder);
        _historyPath = Path.Combine(folder, "history.json");
        _capacity = Math.Max(1, capacity);
    }

    public string HistoryPath => _historyPath;

    public IReadOnlyList<CompressionHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_historyPath))
                return [];

            var json = File.ReadAllText(_historyPath);
            var entries = JsonSerializer.Deserialize<List<CompressionHistoryEntry>>(json, SerializerOptions);
            return entries ?? [];
        }
        catch
        {
            // Bozuk/okunamayan dosya: boş geçmiş gibi davran.
            return [];
        }
    }

    public IReadOnlyList<CompressionHistoryEntry> LoadLastBatch()
    {
        var all = Load();
        if (all.Count == 0)
            return [];

        string lastBatchId = all[^1].BatchId;
        return all.Where(e => e.BatchId == lastBatchId).ToList();
    }

    public string AppendBatch(IEnumerable<CompressionHistoryEntry> entries)
    {
        var all = Load().ToList();
        string batchId = Guid.NewGuid().ToString("N");

        foreach (var entry in entries)
        {
            entry.BatchId = batchId;
            entry.TimestampUtc = DateTime.UtcNow;
            all.Add(entry);
        }

        // Kapasite aşımı: en eski kayıtlar baştan atılır.
        if (all.Count > _capacity)
        {
            all = all.Skip(all.Count - _capacity).ToList();
        }

        Persist(all);
        return batchId;
    }

    public void RemoveBatch(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            return;

        var remaining = Load().Where(e => e.BatchId != batchId).ToList();
        Persist(remaining);
    }

    public void RemoveEntries(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        // T4-M1: açılan dosyalar geçmişten düşülür; yalnızca yol eşleşmesi
        // esas alınır (batch kimliği korunur — kalan girdiler kendi batch'inde kalır).
        var paths = new HashSet<string>(filePaths, StringComparer.OrdinalIgnoreCase);
        if (paths.Count == 0)
            return;

        var remaining = Load().Where(e => !paths.Contains(e.FilePath)).ToList();
        Persist(remaining);
    }

    public void Clear() => Persist([]);

    private void Persist(List<CompressionHistoryEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, SerializerOptions);

        // T2-K6 (inceleme turu 2): yazım hatasında .tmp kalıntısı temizlenir;
        // Move'da overwrite:true TOCTOU yarışını kaldırır.
        var tempPath = _historyPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(_historyPath))
            {
                File.Replace(tempPath, _historyPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _historyPath, overwrite: true);
            }
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // temp zaten taşınmış/kilitli olabilir; kalıntı zararsızdır.
            }

            throw;
        }
    }
}
