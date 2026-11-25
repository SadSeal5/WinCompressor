namespace WinCompressor.Core.Models;

public class ScanProgressReport
{
    public int ScannedFilesCount { get; init; }
    public int ScannedDirectoriesCount { get; init; }
    public int MatchedFilesCount { get; init; }
    public long TotalMatchedBytes { get; init; }
    public string CurrentDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Streaming: a chunk of matched files reported incrementally while the scan
    /// is still running (null when the report carries counters only). Batches let
    /// the UI present results long before the scan finishes on huge trees.
    /// </summary>
    public IReadOnlyList<FileItemInfo>? MatchedFilesBatch { get; init; }
}
