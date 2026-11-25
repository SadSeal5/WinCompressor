namespace WinCompressor.Core.Models;

/// <summary>
/// Persisted application settings. Boundary values (window size/position)
/// are applied by the window layer; scan/algorithm preferences by the ViewModel.
/// </summary>
public class AppSettings
{
    public string LastPath { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string LastAlgorithm { get; set; } = string.Empty;

    public string LastCategory { get; set; } = "All";

    public bool IncludeSubdirectories { get; set; } = true;

    public bool OnlyUncompressed { get; set; } = true;

    public bool IncludeAppData { get; set; }

    public bool ActivityLogVisible { get; set; }

    /// <summary>
    /// Arayüz dili (Faz 5.3): "tr" (varsayılan) veya "en". XAML dil sözlükleri
    /// bu değere göre yüklenir.
    /// </summary>
    public string Language { get; set; } = "tr";

    public double WindowLeft { get; set; } = double.NaN;

    public double WindowTop { get; set; } = double.NaN;

    public double WindowWidth { get; set; } = double.NaN;

    public double WindowHeight { get; set; } = double.NaN;

    public bool WindowMaximized { get; set; }
}
