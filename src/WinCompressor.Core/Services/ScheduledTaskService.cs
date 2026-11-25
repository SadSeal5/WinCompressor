using System.Diagnostics;
using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

/// <summary>
/// Windows Görev Zamanlayıcı'ya schtasks.exe ile haftalık görev kaydeder
/// (Faz 5.1). Görev, WinCompressor'ın CLI modunu yürütür; böylece oyun
/// klasörü gibi hedefler periyodik otomatik sıkıştırılır.
/// </summary>
public class ScheduledTaskService : IScheduledTaskService
{
    /// <summary>Görevler Görev Zamanlayıcı'da bu klasör altında gruplanır.</summary>
    public const string TaskFolder = "WinCompressor";

    private static readonly Dictionary<DayOfWeek, string> DayNames = new()
    {
        [DayOfWeek.Monday] = "MON",
        [DayOfWeek.Tuesday] = "TUE",
        [DayOfWeek.Wednesday] = "WED",
        [DayOfWeek.Thursday] = "THU",
        [DayOfWeek.Friday] = "FRI",
        [DayOfWeek.Saturday] = "SAT",
        [DayOfWeek.Sunday] = "SUN"
    };

    /// <summary>Test seam'i: schtasks çağrısını değiştirir (null = gerçek süreç).</summary>
    private readonly Func<string, (int ExitCode, string Output)>? _runner;

    public ScheduledTaskService(Func<string, (int ExitCode, string Output)>? runner = null)
    {
        _runner = runner;
    }

    public string BuildArguments(ScheduledTaskDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        string taskPath = $"{TaskFolder}\\{definition.TaskName}";
        string commandLine = $"\"{definition.ExecutablePath}\" {definition.Arguments}".Trim();
        string day = DayNames.GetValueOrDefault(definition.DayOfWeek, "MON");

        // /TR tırnaklama: içteki çift tırnaklar \" ile kaçışlanır (schtasks kuralı).
        return $"/Create /F /TN \"{taskPath}\" /TR \"{commandLine.Replace("\"", "\\\"")}\" /SC WEEKLY /D {day} /ST {definition.Time:HH\\:mm}";
    }

    public bool TryCreateWeeklyTask(ScheduledTaskDefinition definition, out string? error)
    {
        error = null;
        var arguments = BuildArguments(definition);

        try
        {
            int exitCode;
            string output;

            if (_runner != null)
            {
                (exitCode, output) = _runner(arguments);
            }
            else
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });

                if (process is null)
                {
                    error = "schtasks.exe başlatılamadı.";
                    return false;
                }

                // K8 (inceleme bulgusu): sıralı ReadToEnd çocuk süreç stderr'i
                // doldururken stdout'u beklerken pipe-deadlock üretir; her iki
                // akış eşzamanlı okunur.
                // T3-M1 (inceleme turu 3): önceki sıra "akışları .Result ile
                // bekle → WaitForExit(timeout)" asılı süreçte ölümdü — asılı
                // schtasks pipe'ları açık tuttuğundan .Result süresiz blokluyor
                // ve 10 sn Kill dalına hiç ulaşılamıyordu. Doğru sıra (CompactProcess
                // deseni): önce süreç beklenir; asılıysa Kill sonrası akışlar
                // toplanır (Kill, çocuk handle'larını kapatır → ReadToEnd EOF).
                var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
                var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());

                if (!process.WaitForExit(10_000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Süreç bu arada zaten çıkmış olabilir.
                    }

                    process.WaitForExit();
                    output = stdoutTask.Result + stderrTask.Result;
                    error = "schtasks.exe 10 saniye içinde yanıt vermedi; süreç sonlandırıldı.";
                    return false;
                }

                output = stdoutTask.Result + stderrTask.Result;
                exitCode = process.ExitCode;
            }

            if (exitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(output)
                    ? $"schtasks {exitCode} koduyla döndü."
                    : output.Trim();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
