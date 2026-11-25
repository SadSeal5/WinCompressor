using System.Xml.Linq;

namespace WinCompressor.Tests;

/// <summary>
/// Faz 5.3 (i18n tam kapsam): TR/EN sözlük paritesi ve MainWindow.xaml'in
/// başvurduğu tüm anahtarların iki sözlükte de bulunması. XAML'ler kaynak
/// ağacından XDocument ile okunur (WPF yükleme / STA gerekmez; kod vs.
/// BAML farkı olmadan statik doğrulama yapar).
/// </summary>
public class LocalizationParityTests
{
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Repo kökü (.git) bulunamadı; testler repo dışından mı koşuyor?");
    }

    private static string UiProjectDir() =>
        Path.Combine(FindRepoRoot(), "src", "WinCompressor.UI");

    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static IEnumerable<string> LoadKeys(string relativePath)
    {
        string path = Path.Combine(UiProjectDir(), relativePath);
        var doc = XDocument.Load(path);
        return doc
            .Descendants()
            .Where(e => e.Attribute(XamlNamespace + "Key") is not null)
            .Select(e => e.Attribute(XamlNamespace + "Key")!.Value)
            .ToList();
    }

    private static HashSet<string> LoadMainWindowDynamicResourceKeys()
    {
        string path = Path.Combine(UiProjectDir(), "MainWindow.xaml");
        var doc = XDocument.Load(path);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attr in doc.Descendants().Attributes())
        {
            string value = attr.Value;
            int idx = 0;
            while ((idx = value.IndexOf("{DynamicResource ", idx, StringComparison.Ordinal)) >= 0)
            {
                int start = idx + "{DynamicResource ".Length;
                int end = value.IndexOf('}', start);
                if (end < 0)
                    break;

                keys.Add(value[start..end].Trim());
                idx = end;
            }
        }

        return keys;
    }

    private static string FormatSet(HashSet<string> set) =>
        string.Join(", ", set.OrderBy(k => k, StringComparer.Ordinal));

    [Fact]
    public void TrAndEnDictionaries_HaveIdenticalKeySets()
    {
        var tr = new HashSet<string>(LoadKeys("Localization/Strings.tr.xaml"), StringComparer.Ordinal);
        var en = new HashSet<string>(LoadKeys("Localization/Strings.en.xaml"), StringComparer.Ordinal);

        var missingInEn = tr.Except(en).ToList();
        var missingInTr = en.Except(tr).ToList();

        Assert.True(missingInEn.Count == 0,
            $"EN sözlüğünde eksik anahtarlar: {string.Join(", ", missingInEn)}");
        Assert.True(missingInTr.Count == 0,
            $"TR sözlüğünde eksik anahtarlar: {string.Join(", ", missingInTr)}");
    }

    [Fact]
    public void Dictionaries_ContainNoDuplicateKeys()
    {
        var trKeys = LoadKeys("Localization/Strings.tr.xaml").ToList();
        var enKeys = LoadKeys("Localization/Strings.en.xaml").ToList();

        Assert.Equal(trKeys.Count, trKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(enKeys.Count, enKeys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MainWindowDynamicResourceKeys_AllExistInBothDictionaries()
    {
        // Faz 5.3 tam kapsam: MainWindow.xaml'deki her {DynamicResource X}
        // iki sözlükte de tanımlı olmalı — yoksa başlangıçta sessizce boş
        // metin görünür (WPF eksik anahtarta hata vermez).
        var referenced = LoadMainWindowDynamicResourceKeys();
        var tr = new HashSet<string>(LoadKeys("Localization/Strings.tr.xaml"), StringComparer.Ordinal);
        var en = new HashSet<string>(LoadKeys("Localization/Strings.en.xaml"), StringComparer.Ordinal);

        Assert.True(referenced.Count > 10,
            "MainWindow.xaml'de beklenenden az DynamicResource başvurusu bulundu; ayrıştırıcı bozulmuş olabilir.");

        var missingInTr = referenced.Except(tr).ToList();
        var missingInEn = referenced.Except(en).ToList();

        Assert.True(missingInTr.Count == 0,
            $"TR sözlüğünde eksik anahtarlar: {FormatSet([.. missingInTr])}");
        Assert.True(missingInEn.Count == 0,
            $"EN sözlüğünde eksik anahtarlar: {FormatSet([.. missingInEn])}");
    }
}
