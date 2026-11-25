using System.IO;
using System.Text;

namespace WinCompressor.UI.ViewModels;

/// <summary>
/// İşlem günlüğü kısmı: seviyeli kayıt ve .txt / .csv dışa aktarma.
/// </summary>
public partial class MainViewModel
{
    public bool IsActivityLogVisible
    {
        get => _isActivityLogVisible;
        set => SetProperty(ref _isActivityLogVisible, value);
    }

    public void LogActivity(string message, ActivityLogLevel level = ActivityLogLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        ActivityLogs.Add(new ActivityLogEntry(message, level));
    }

    public void LogWarning(string message) => LogActivity(message, ActivityLogLevel.Warning);

    public void LogError(string message) => LogActivity(message, ActivityLogLevel.Error);

    /// <summary>
    /// Exports the activity log to a .txt (display lines) or .csv
    /// (Zaman;Seviye;Mesaj) file. The destination path comes from the
    /// <see cref="ExportLogsPathPicker"/> test seam or a SaveFileDialog.
    /// </summary>
    public void ExecuteExportActivityLogs()
    {
        if (ActivityLogs.Count == 0)
        {
            StatusMessage = "Dışa aktarılacak günlük kaydı yok.";
            return;
        }

        string? targetPath;

        if (ExportLogsPathPicker != null)
        {
            // Seam yapılandırılmışsa (testler / entegrasyon) sonucu bağlayıcıdır;
            // null dönüş = kullanıcı iptali. Asla dialog açılmaz.
            targetPath = ExportLogsPathPicker();
            if (string.IsNullOrWhiteSpace(targetPath))
                return;
        }
        else
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "İşlem Günlüğünü Dışa Aktar",
                Filter = "Metin dosyası (*.txt)|*.txt|CSV dosyası (*.csv)|*.csv",
                FileName = $"WinCompressor_Log_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt"
            };

            if (dialog.ShowDialog() != true)
                return;

            targetPath = dialog.FileName;
        }

        try
        {
            bool isCsv = targetPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

            var lines = new List<string>(ActivityLogs.Count + 1);

            if (isCsv)
            {
                lines.Add("Zaman;Seviye;Mesaj");
                foreach (var entry in ActivityLogs)
                {
                    lines.Add($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss};{entry.Level};{EscapeCsv(entry.Message)}");
                }
            }
            else
            {
                foreach (var entry in ActivityLogs)
                {
                    lines.Add(entry.DisplayText);
                }
            }

            File.WriteAllLines(targetPath, lines, new UTF8Encoding(true));

            StatusMessage = $"Günlük dışa aktarıldı: {targetPath}";
            LogActivity($"Günlük dışa aktarıldı ({ActivityLogs.Count} kayıt): {targetPath}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Günlük dışa aktarılamadı: {ex.Message}";
            LogError($"Günlük dışa aktarma hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// T3-N2 (inceleme turu 3): satır sonu karakterleri CSV satır yapısını bozduğu
    /// için (çok satırlı hata mesajları dışa aktarılan kayıtların önemli bölümüdür)
    /// boşluğa indirgenir; ayraç/tırnak kaçışlaması RFC 4180 kalıbındadır.
    /// </summary>
    public static string EscapeCsv(string value)
    {
        string normalized = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        return normalized.Contains(';') || normalized.Contains('"')
            ? $"\"{normalized.Replace("\"", "\"\"")}\""
            : normalized;
    }
}
