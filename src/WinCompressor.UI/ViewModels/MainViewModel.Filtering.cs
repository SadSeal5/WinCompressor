using WinCompressor.Core.Models;

namespace WinCompressor.UI.ViewModels;

/// <summary>
/// Arama metni ve kategori chip'i filtreleme kısmı.
/// </summary>
public partial class MainViewModel
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".ocx"
    };

    private static readonly HashSet<string> TextCodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".json", ".xml", ".csv", ".cs", ".js", ".ts", ".py", ".cpp", ".h", ".html", ".css"
    };

    private static readonly HashSet<string> DataMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dat", ".bin", ".pak", ".assets", ".vmdk", ".iso", ".db", ".sqlite"
    };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
                (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public FileCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// Cached filtered item count. Recomputed once per ApplyFilter() instead of
    /// enumerating the whole view on every property read.
    /// </summary>
    public int FilteredFilesCount => _filteredFilesCount;

    public string FilterCountText => Files.Count == FilteredFilesCount
        ? $"{Files.Count} dosya"
        : $"{FilteredFilesCount} / {Files.Count} dosya";

    public bool IsFilterActive => !string.IsNullOrWhiteSpace(SearchText) || SelectedCategory != FileCategory.All;

    public void SetCategory(FileCategory category) => SelectedCategory = category;

    public void SetCategory(string category)
    {
        if (Enum.TryParse<FileCategory>(category, true, out var parsed))
        {
            SelectedCategory = parsed;
        }
    }

    private void ExecuteSelectCategory(object? param)
    {
        if (param is FileCategory cat)
        {
            SelectedCategory = cat;
        }
        else if (param is string catStr && Enum.TryParse<FileCategory>(catStr, true, out var parsed))
        {
            SelectedCategory = parsed;
        }
    }

    public bool FilterFileItem(object itemObj)
    {
        if (itemObj is not FileItemViewModel item)
            return false;

        if (!MatchesCategory(item, SelectedCategory))
            return false;

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var query = _searchText.Trim();
            bool matchName = item.FileName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
            bool matchExt = item.Extension?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
            bool matchPath = item.FilePath?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

            if (!matchName && !matchExt && !matchPath)
                return false;
        }

        return true;
    }

    public static bool MatchesCategory(FileItemViewModel item, FileCategory category)
    {
        return category switch
        {
            FileCategory.All => true,
            FileCategory.Executables => IsExecutable(item.Extension),
            FileCategory.LargeFiles => item.Size >= LargeFileThreshold,
            FileCategory.TextCode => IsTextCode(item.Extension),
            FileCategory.DataMedia => IsDataMedia(item.Extension),
            FileCategory.CompactOs => item.CurrentAlgorithm.IsWofAlgorithm(),
            _ => true
        };
    }

    private static string NormalizeExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
            return string.Empty;
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    public static bool IsExecutable(string? ext) =>
        ExecutableExtensions.Contains(NormalizeExtension(ext));

    public static bool IsTextCode(string? ext) =>
        TextCodeExtensions.Contains(NormalizeExtension(ext));

    public static bool IsDataMedia(string? ext) =>
        DataMediaExtensions.Contains(NormalizeExtension(ext));

    public void ApplyFilter()
    {
        FilesView.Refresh();
        _filteredFilesCount = FilesView.OfType<object>().Count();
        OnPropertyChanged(nameof(FilteredFilesCount));
        OnPropertyChanged(nameof(FilterCountText));
        OnPropertyChanged(nameof(IsFilterActive));
    }
}
