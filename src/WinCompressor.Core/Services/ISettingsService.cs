using WinCompressor.Core.Models;

namespace WinCompressor.Core.Services;

public interface ISettingsService
{
    /// <summary>Loads settings; returns defaults when the file is missing or corrupt.</summary>
    AppSettings Load();

    /// <summary>Persists settings atomically (temp file + replace).</summary>
    void Save(AppSettings settings);
}
