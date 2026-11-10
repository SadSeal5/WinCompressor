using System.IO;
using WinCompressor.Core.Models;
using WinCompressor.Core.Native;

namespace WinCompressor.Core.Services;

public class FileScannerService : IFileScannerService
{
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
        return Task.Run<IReadOnlyList<FileItemInfo>>(() =>
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

                            // Skip excluded directories
                            if (options.ExcludedDirectories.Contains(subDir.Name))
                                continue;

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

            progress?.Report(new ScanProgressReport
            {
                ScannedFilesCount = scannedFilesCount,
                ScannedDirectoriesCount = scannedDirsCount,
                MatchedFilesCount = sortedList.Count,
                TotalMatchedBytes = totalMatchedBytes,
                CurrentDirectory = options.RootPath
            });

            return sortedList;
        }, cancellationToken);
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
        bool isCompressed = (allocatedSize >= 0 && allocatedSize < fileInfo.Length) ||
                            Kernel32.IsCompressed(fileInfo.Attributes);

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
            CurrentAlgorithm = isCompressed ? CompressionAlgorithm.Ntfs : CompressionAlgorithm.None,
            IsCompressed = isCompressed,
            Status = isCompressed ? "Compressed" : "Pending"
        };

        return true;
    }
}
