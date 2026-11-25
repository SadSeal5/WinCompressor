using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface IScheduledTaskService
{
    /// <summary>Yerel `schtasks /Create` bağımsız değişkenleri (test edilebilir, saf).</summary>
    string BuildArguments(ScheduledTaskDefinition definition);

    /// <summary>
    /// Haftalık görevi oluşturur. Standart kullanıcı için de çalışır
    /// (yükseltilmiş ayrıcalık istemez); başarısızlıkta hata mesajı döner.
    /// </summary>
    bool TryCreateWeeklyTask(ScheduledTaskDefinition definition, out string? error);
}
