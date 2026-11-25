namespace WinCompressor.Core.Models;

public class ScanOptions
{
    public required string RootPath { get; set; }
    public bool IncludeSubdirectories { get; set; } = true;
    public long MinFileSize { get; set; } = 4096;
    public bool OnlyUncompressed { get; set; } = true;

    /// <summary>
    /// When true, "AppData" directories are scanned as well (it is excluded
    /// by default together with $Recycle.Bin and system folders).
    /// </summary>
    public bool IncludeAppData { get; set; }

    /// <summary>
    /// Streaming batch size: matched files are reported to progress listeners in
    /// chunks of this many items while the scan runs. Default 500 balances UI
    /// responsiveness against view-refresh cost on 100k+ file trees.
    /// </summary>
    public int FileReportBatchSize { get; set; } = 500;

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
