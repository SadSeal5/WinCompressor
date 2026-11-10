namespace WinCompressor.Core.Models;

public class ScanOptions
{
    public required string RootPath { get; set; }
    public bool IncludeSubdirectories { get; set; } = true;
    public long MinFileSize { get; set; } = 4096;
    public bool OnlyUncompressed { get; set; } = true;

    public HashSet<string> ExcludedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".gz", ".tar",
        ".mp4", ".mkv", ".avi", ".mov",
        ".mp3", ".flac",
        ".jpg", ".jpeg", ".png", ".webp",
        ".pdf"
    };

    public HashSet<string> ExcludedDirectories { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        "System Volume Information",
        ".git",
        ".vs",
        "node_modules",
        "AppData"
    };
}
