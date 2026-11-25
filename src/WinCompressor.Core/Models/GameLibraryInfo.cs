namespace WinCompressor.Core.Models;

/// <summary>Kurulu bir oyun kütüphanesini (Steam / Epic / ...) tanımlar.</summary>
public record GameLibraryInfo(
    string Name,
    string GamesPath,
    string Emoji)
{
    /// <summary>UI'da buton metni: "🎮 Steam".</summary>
    public string DisplayName => $"{Emoji} {Name}";
}
