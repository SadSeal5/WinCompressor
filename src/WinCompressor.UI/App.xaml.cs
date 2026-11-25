using System.Runtime.InteropServices;
using System.Windows;
using WinCompressor.Core.Cli;
using WinCompressor.Core.Services;

namespace WinCompressor.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Faz 5.1/5.2: "WinCompressor.exe cli ..." — pencere açmadan betik modu.
        // Görev Zamanlayıcı bu modu çağırır; tek EXE hem GUI hem CLI olur.
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "cli", StringComparison.OrdinalIgnoreCase))
        {
            AttachParentConsole();
            int exitCode;
            try
            {
                // Threadpool'da koştur: WPF dispatcher SynchronizationContext'i
                // altında .GetAwaiter().GetResult() deadlock yaratır (devam edişleri
                // bloklanan dispatcher'a postalanır). Task.Run sync context'i
                // sıfırlar — async CLI kodu güvenle senkron beklenir.
                exitCode = System.Threading.Tasks.Task.Run(async () =>
                    await CliDispatcher.CreateDefault().RunAsync(e.Args[1..])).GetAwaiter().GetResult();
            }
            catch
            {
                exitCode = 2;
            }
            finally
            {
                FreeConsole();
            }

            Shutdown(exitCode);
            return;
        }

        // Faz 5.3: kalıcı dil tercihini yükle (XAML dil sözlüğü).
        ApplyLanguage(LoadPersistedLanguage());

        base.OnStartup(e);
    }

    /// <summary>
    /// Dil sözlüğünü ("Strings.&lt;lang&gt;.xaml") application kaynaklarına ekler;
    /// XAML tarafı DynamicResource ile bağlanır. Bilinmeyen dil → Türkçe.
    /// </summary>
    public void ApplyLanguage(string? language)
    {
        string lang = string.IsNullOrWhiteSpace(language)
            ? "tr"
            : language.Trim().ToLowerInvariant();

        var existing = Resources.MergedDictionaries
            .Where(d => d.Source is not null && d.Source.OriginalString.Contains("Localization/Strings.", StringComparison.Ordinal))
            .ToList();

        foreach (var dictionary in existing)
        {
            Resources.MergedDictionaries.Remove(dictionary);
        }

        var uri = new Uri($"pack://application:,,,/Localization/Strings.{lang}.xaml", UriKind.Absolute);

        try
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        }
        catch
        {
            // Sözlük yok: varsayılan Türkçeyle devam.
            if (lang != "tr")
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Localization/Strings.tr.xaml", UriKind.Absolute)
                });
            }
        }
    }

    private static string? LoadPersistedLanguage()
    {
        try
        {
            return new JsonSettingsService().Load().Language;
        }
        catch
        {
            return null;
        }
    }

    // Birincil konsola bağlan (kullanıcı terminalden çalıştırdıysa çıktı görünür;
    // Görev Zamanlayıcı'dan çağrıda sessizce başarısız olur, zarar yok).
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private static void AttachParentConsole() => AttachConsole(AttachParentProcess);
}
