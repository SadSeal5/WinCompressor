namespace WinCompressor.Core.Models;

/// <summary>
/// Zamanlanmış haftalık sıkıştırma görevi tanımı (Faz 5.1). Görev
/// Windows Görev Zamanlayıcı'ya `schtasks /Create` ile kaydedilir ve
/// WinCompressor CLI modunu çağırır.
/// </summary>
public record ScheduledTaskDefinition(
    string TaskName,
    string ExecutablePath,
    string Arguments,
    DayOfWeek DayOfWeek,
    TimeOnly Time);
