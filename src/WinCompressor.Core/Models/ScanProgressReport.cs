namespace WinCompressor.Core.Models;

public class ScanProgressReport
{
    public int ScannedFilesCount { get; init; }
    public int ScannedDirectoriesCount { get; init; }
    public int MatchedFilesCount { get; init; }
    public long TotalMatchedBytes { get; init; }
    public string CurrentDirectory { get; init; } = string.Empty;
}
