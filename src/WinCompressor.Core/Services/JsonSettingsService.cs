using System.Text.Json;
using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

/// <summary>
/// JSON-file backed settings store (defaults to %AppData%\WinCompressor\settings.json).
/// Missing or corrupt files fall back to defaults; saves are atomic.
/// </summary>
public class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Pencere sınırları ilk çalıştırmada double.NaN ("ayarlanmadı") olabilir;
        // NaN/Infinity literal olarak yazılp okunabilsin (yoksa Save ArgumentException fırlatır).
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly string _settingsPath;

    public JsonSettingsService(string? settingsFolder = null)
    {
        var folder = settingsFolder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinCompressor");

        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return settings ?? new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable file: behave like first run.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(settings, SerializerOptions);

        // T2-K6 (inceleme turu 2): yazım hatasında .tmp kalıntı bırakmamak için
        // temizlik garanti edilir; Move'da overwrite:true ile File.Exists/Move
        // arasındaki TOCTOU yarışı kaldırılır.
        var tempPath = _settingsPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _settingsPath, overwrite: true);
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
