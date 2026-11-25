using System.IO;
using WinCompressor.Core.Models;
using WinCompressor.Core.Native;

namespace WinCompressor.Core.Services;

public class FileScannerService : IFileScannerService
{
    private const int DetectionParallelism = 4;

    private readonly IAlgorithmDetectionService? _algorithmDetectionService;

    public FileScannerService(IAlgorithmDetectionService? algorithmDetectionService = null)
    {
        _algorithmDetectionService = algorithmDetectionService;
    }

    public bool IsRootDrive(string path) => CheckIsRootDrive(path);

    public static bool CheckIsRootDrive(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return true;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var trimmedFull = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(trimmedFull, trimmedRoot, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Invalid path syntax
        }

        return false;
    }

    public Task<IReadOnlyList<FileItemInfo>> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<FileItemInfo>>(async () =>
        {
            var matchedFiles = new List<FileItemInfo>();

            if (string.IsNullOrWhiteSpace(options.RootPath))
                return matchedFiles;

            int scannedFilesCount = 0;
            int scannedDirsCount = 0;
            long totalMatchedBytes = 0;

            // If target is a single file, evaluate and return it directly
            if (File.Exists(options.RootPath))
            {
                if (!CheckIsRootDrive(options.RootPath))
                {
                    try
                    {
                        var fi = new FileInfo(options.RootPath);
                        scannedFilesCount = 1;
                        if (ProcessFile(fi, options, out var item))
                        {
                            matchedFiles.Add(item!);
                            totalMatchedBytes += item!.Size;
                        }
                    }
                    catch
                    {
                        // Ignore unreadable file
                    }
                }

                ResolveWofAlgorithms(matchedFiles, cancellationToken);

                progress?.Report(new ScanProgressReport
                {
                    ScannedFilesCount = scannedFilesCount,
                    ScannedDirectoriesCount = scannedDirsCount,
                    MatchedFilesCount = matchedFiles.Count,
                    TotalMatchedBytes = totalMatchedBytes,
                    CurrentDirectory = Path.GetDirectoryName(options.RootPath) ?? string.Empty
                });

                return matchedFiles;
            }

            if (!Directory.Exists(options.RootPath))
                return matchedFiles;

            var dirQueue = new Queue<string>();
            dirQueue.Enqueue(options.RootPath);

            // Streaming buffer of matched files awaiting the next batch flush.
            var streamedBatch = new List<FileItemInfo>();

            while (dirQueue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentDir = dirQueue.Dequeue();
                scannedDirsCount++;

                var dirInfo = new DirectoryInfo(currentDir);

                // Enumerate subdirectories safely
                if (options.IncludeSubdirectories)
                {
                    try
                    {
                        foreach (var subDir in dirInfo.EnumerateDirectories())
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // Skip reparse points (symlinks / junctions) to prevent recursive traversal loops
                            if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                                continue;

                            // Skip excluded directories ("AppData" re-enters when
                            // the user explicitly opted in).
                            bool isAppData = subDir.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase);
                            if (options.ExcludedDirectories.Contains(subDir.Name) &&
                                !(isAppData && options.IncludeAppData))
                            {
                                continue;
                            }

                            dirQueue.Enqueue(subDir.FullName);
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
                    {
                        // Inaccessible system folder; continue with remaining items
                    }
                }

                // Enumerate files safely in current directory
                try
                {
                    foreach (var fileInfo in dirInfo.EnumerateFiles())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        scannedFilesCount++;

                        if (ProcessFile(fileInfo, options, out var item))
                        {
                            matchedFiles.Add(item!);
                            totalMatchedBytes += item!.Size;
                            streamedBatch.Add(item!);

                            // Streaming: flush matched files to the UI in chunks so
                            // results appear while the scan is still running.
                            int batchSize = Math.Max(1, options.FileReportBatchSize);
                            if (streamedBatch.Count >= batchSize)
                            {
                                progress?.Report(new ScanProgressReport
                                {
                                    ScannedFilesCount = scannedFilesCount,
                                    ScannedDirectoriesCount = scannedDirsCount,
                                    MatchedFilesCount = matchedFiles.Count,
                                    TotalMatchedBytes = totalMatchedBytes,
                                    CurrentDirectory = currentDir,
                                    MatchedFilesBatch = streamedBatch.ToArray()
                                });
                                streamedBatch.Clear();
                            }
                        }

                        if (scannedFilesCount % 100 == 0)
                        {
                            progress?.Report(new ScanProgressReport
                            {
                                ScannedFilesCount = scannedFilesCount,
                                ScannedDirectoriesCount = scannedDirsCount,
                                MatchedFilesCount = matchedFiles.Count,
                                TotalMatchedBytes = totalMatchedBytes,
                                CurrentDirectory = currentDir
                            });
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
                {
                    // Inaccessible files; continue with remaining items
                }

                progress?.Report(new ScanProgressReport
                {
                    ScannedFilesCount = scannedFilesCount,
                    ScannedDirectoriesCount = scannedDirsCount,
                    MatchedFilesCount = matchedFiles.Count,
                    TotalMatchedBytes = totalMatchedBytes,
                    CurrentDirectory = currentDir
                });
            }

            // CRITICAL USER REQUIREMENT: Results MUST be ordered strictly descending by file size
            var sortedList = matchedFiles.OrderByDescending(f => f.Size).ToList();

            ResolveWofAlgorithms(sortedList, cancellationToken);

            progress?.Report(new ScanProgressReport
            {
                ScannedFilesCount = scannedFilesCount,
                ScannedDirectoriesCount = scannedDirsCount,
                MatchedFilesCount = sortedList.Count,
                TotalMatchedBytes = totalMatchedBytes,
                CurrentDirectory = options.RootPath,
                MatchedFilesBatch = streamedBatch.Count > 0 ? streamedBatch.ToArray() : null
            });
            streamedBatch.Clear();

            return sortedList;
        }, cancellationToken);
    }

    /// <summary>
    /// Resolves the exact CompactOS algorithm of WOF-suspect files (allocated size
    /// smaller than logical size without the NTFS Compressed attribute) by probing
    /// FSCTL_GET_EXTERNAL_BACKING in parallel. Files confirmed as WOF get their real
    /// algorithm (LZX / XPRESS); files that turn out not to be WOF-backed (e.g.
    /// sparse files) are corrected back to uncompressed state.
    /// </summary>
    private void ResolveWofAlgorithms(List<FileItemInfo> files, CancellationToken cancellationToken)
    {
        if (_algorithmDetectionService is null)
            return;

        var suspects = files
            .Where(f => f.CurrentAlgorithm == CompressionAlgorithm.Unknown)
            .ToList();

        if (suspects.Count == 0)
            return;

        Parallel.ForEach(
            suspects,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = DetectionParallelism,
                CancellationToken = cancellationToken
            },
            item =>
            {
                try
                {
                    var algorithm = _algorithmDetectionService.GetCompactOsAlgorithm(item.FilePath);

                    if (algorithm is not null)
                    {
                        // Confirmed WOF/CompactOS compression with a known algorithm.
                        item.CurrentAlgorithm = algorithm.Value;
                        item.IsCompressed = true;
                        item.Status = "Compressed";
                    }
                    else
                    {
                        // Not WOF-backed: allocated < size without the Compressed
                        // attribute means a sparse file, not compression. Correct
                        // the model so savings are not fabricated for it.
                        item.CurrentAlgorithm = CompressionAlgorithm.None;
                        item.IsCompressed = false;
                        item.Status = "Pending";
                        item.AllocatedSize = item.Size;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Probe failed (locked file, permission): leave as Unknown.
                }
            });
    }

    private static bool ProcessFile(FileInfo fileInfo, ScanOptions options, out FileItemInfo? item)
    {
        item = null;

        // INVARIANT: Never return drive roots or directories
        if (CheckIsRootDrive(fileInfo.FullName))
            return false;

        // Check minimum file size
        if (fileInfo.Length < options.MinFileSize)
            return false;

        // Check excluded extensions
        if (options.ExcludedExtensions.Contains(fileInfo.Extension))
            return false;

        long allocatedSize = Kernel32.GetDiskAllocatedSize(fileInfo.FullName);
        bool ntfsCompressed = Kernel32.IsCompressed(fileInfo.Attributes);
        bool looksWofCompressed = allocatedSize >= 0 && allocatedSize < fileInfo.Length && !ntfsCompressed;
        bool isCompressed = ntfsCompressed || looksWofCompressed;

        // Filter out already compressed files if looking for uncompressed files only
        if (options.OnlyUncompressed && isCompressed)
            return false;

        item = new FileItemInfo
        {
            FilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            Extension = fileInfo.Extension,
            Size = fileInfo.Length,
            AllocatedSize = allocatedSize >= 0 ? allocatedSize : fileInfo.Length,
            // NTFS compression carries the Compressed attribute; WOF (CompactOS)
            // files do not, so their exact algorithm (LZX / XPRESS) is Unknown
            // until the post-scan format probe resolves it.
            CurrentAlgorithm = ntfsCompressed
                ? CompressionAlgorithm.Ntfs
                : looksWofCompressed
                    ? CompressionAlgorithm.Unknown
                    : CompressionAlgorithm.None,
            IsCompressed = isCompressed,
            Status = isCompressed ? "Compressed" : "Pending"
        };

        return true;
    }
}
