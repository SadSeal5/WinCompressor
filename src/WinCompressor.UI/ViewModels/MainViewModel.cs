using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.Converters;

namespace WinCompressor.UI.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IFileScannerService _fileScanner;
    private readonly ICompressionService _compressionService;
    private readonly IAdminService _adminService;

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

    public const long LargeFileThreshold = 50L * 1024L * 1024L; // 50 MB

    private string _selectedPath = string.Empty;
    private bool _includeSubdirectories = true;
    private bool _onlyUncompressed = true;
    private bool _isScanning;
    private bool _isCompressing;
    private string _statusMessage = "Hazır";
    private string _currentActionText = "Taramaya başlamak için bir klasör seçin ve 'TARA' butonuna tıklayın.";
    private double _progressPercentage;
    private string _progressText = string.Empty;
    private int _totalFilesFound;
    private long _totalBytesFound;
    private long _totalBytesSaved;
    private CompressionAlgorithm _selectedAlgorithm;
    private bool _isAdministrator;
    private int _selectedFilesCount;
    private long _selectedFilesTotalSize;
    private long _selectedFilesBytesSaved;
    private bool? _isAllSelected = false;

    private string _searchText = string.Empty;
    private FileCategory _selectedCategory = FileCategory.All;
    private bool _isActivityLogVisible;

    private long _totalUncompressedBytes;
    private long _totalCompressedBytes;
    private double _savingsPercentage;
    private string _savingsPercentageText = "%0 Alan Tasarrufu";

    private CancellationTokenSource? _activeCts;
    private bool _isUpdatingSelectionAll;

    public Func<string?>? FolderPicker { get; set; }
    public Action<string>? OpenFileLocationHandler { get; set; }
    public Action<string>? ClipboardHandler { get; set; }

    public MainViewModel()
        : this(new FileScannerService(), new CompressionService(), new AdminService())
    {
    }

    public MainViewModel(
        IFileScannerService fileScanner,
        ICompressionService compressionService,
        IAdminService adminService)
    {
        _fileScanner = fileScanner ?? throw new ArgumentNullException(nameof(fileScanner));
        _compressionService = compressionService ?? throw new ArgumentNullException(nameof(compressionService));
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));

        Files = new ObservableCollection<FileItemViewModel>();
        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.Filter = FilterFileItem;
        FilesView.SortDescriptions.Clear();
        FilesView.SortDescriptions.Add(new SortDescription(nameof(FileItemViewModel.Size), ListSortDirection.Descending));

        AlgorithmOptions = new ObservableCollection<AlgorithmOptionViewModel>();
        ActivityLogs = new ObservableCollection<string>();

        _isAdministrator = _adminService.IsAdministrator();
        _selectedAlgorithm = _isAdministrator ? CompressionAlgorithm.Lzx : CompressionAlgorithm.Ntfs;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _selectedPath = Directory.Exists(userProfile) ? userProfile : "C:\\";

        InitializeAlgorithmOptions();
        InitializeCommands();
        LogActivity("WinCompressor başlatıldı. Sistem hazır.");
    }

    public ICollectionView FilesView { get; }
    public ObservableCollection<FileItemViewModel> Files { get; }
    public ObservableCollection<AlgorithmOptionViewModel> AlgorithmOptions { get; }
    public ObservableCollection<string> ActivityLogs { get; }

    public string SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (SetProperty(ref _selectedPath, value))
            {
                (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set => SetProperty(ref _includeSubdirectories, value);
    }

    public bool OnlyUncompressed
    {
        get => _onlyUncompressed;
        set => SetProperty(ref _onlyUncompressed, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RaiseCommandStatesChanged();
            }
        }
    }

    public bool IsCompressing
    {
        get => _isCompressing;
        private set
        {
            if (SetProperty(ref _isCompressing, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RaiseCommandStatesChanged();
            }
        }
    }

    public bool IsBusy => _isScanning || _isCompressing;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentActionText
    {
        get => _currentActionText;
        set => SetProperty(ref _currentActionText, value);
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set => SetProperty(ref _progressPercentage, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public int TotalFilesFound
    {
        get => _totalFilesFound;
        set => SetProperty(ref _totalFilesFound, value);
    }

    public long TotalBytesFound
    {
        get => _totalBytesFound;
        set => SetProperty(ref _totalBytesFound, value);
    }

    public long TotalBytesSaved
    {
        get => _totalBytesSaved;
        set
        {
            if (SetProperty(ref _totalBytesSaved, value))
            {
                OnPropertyChanged(nameof(TotalSavedBytes));
            }
        }
    }

    public long TotalSavedBytes
    {
        get => _totalBytesSaved;
        set => TotalBytesSaved = value;
    }

    public CompressionAlgorithm SelectedAlgorithm
    {
        get => _selectedAlgorithm;
        set => SetProperty(ref _selectedAlgorithm, value);
    }

    public bool IsAdministrator
    {
        get => _isAdministrator;
        set => SetProperty(ref _isAdministrator, value);
    }

    public int SelectedFilesCount
    {
        get => _selectedFilesCount;
        private set => SetProperty(ref _selectedFilesCount, value);
    }

    public long SelectedFilesTotalSize
    {
        get => _selectedFilesTotalSize;
        private set => SetProperty(ref _selectedFilesTotalSize, value);
    }

    public long SelectedFilesBytesSaved
    {
        get => _selectedFilesBytesSaved;
        private set => SetProperty(ref _selectedFilesBytesSaved, value);
    }

    public bool? IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (SetProperty(ref _isAllSelected, value) && value.HasValue && !_isUpdatingSelectionAll)
            {
                SetAllItemsSelection(value.Value);
            }
        }
    }

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

    public int FilteredFilesCount => FilesView?.Cast<object>().Count() ?? Files.Count;

    public string FilterCountText => Files.Count == FilteredFilesCount
        ? $"{Files.Count} dosya"
        : $"{FilteredFilesCount} / {Files.Count} dosya";

    public bool IsFilterActive => !string.IsNullOrWhiteSpace(SearchText) || SelectedCategory != FileCategory.All;

    public bool IsActivityLogVisible
    {
        get => _isActivityLogVisible;
        set => SetProperty(ref _isActivityLogVisible, value);
    }

    public long TotalUncompressedBytes
    {
        get => _totalUncompressedBytes;
        private set => SetProperty(ref _totalUncompressedBytes, value);
    }

    public long TotalCompressedBytes
    {
        get => _totalCompressedBytes;
        private set => SetProperty(ref _totalCompressedBytes, value);
    }

    public double SavingsPercentage
    {
        get => _savingsPercentage;
        private set => SetProperty(ref _savingsPercentage, value);
    }

    public string SavingsPercentageText
    {
        get => _savingsPercentageText;
        private set => SetProperty(ref _savingsPercentageText, value);
    }

    public GridLength CompressedWidthRatio => TotalUncompressedBytes > 0 && TotalCompressedBytes > 0
        ? new GridLength(Math.Max(1, TotalCompressedBytes), GridUnitType.Star)
        : new GridLength(1, GridUnitType.Star);

    public GridLength SavedWidthRatio => TotalUncompressedBytes > 0 && TotalSavedBytes > 0
        ? new GridLength(Math.Max(1, TotalSavedBytes), GridUnitType.Star)
        : new GridLength(0, GridUnitType.Star);

    public ICommand BrowseFolderCommand { get; private set; } = null!;
    public ICommand QuickSelectDriveCommand { get; private set; } = null!;
    public ICommand ScanCommand { get; private set; } = null!;
    public ICommand CancelCommand { get; private set; } = null!;
    public ICommand CompressSelectedCommand { get; private set; } = null!;
    public ICommand DecompressSelectedCommand { get; private set; } = null!;
    public ICommand SelectAllCommand { get; private set; } = null!;
    public ICommand DeselectAllCommand { get; private set; } = null!;
    public ICommand InvertSelectionCommand { get; private set; } = null!;
    public ICommand SelectLargeFilesCommand { get; private set; } = null!;
    public ICommand ClearSearchCommand { get; private set; } = null!;
    public ICommand SelectCategoryCommand { get; private set; } = null!;
    public ICommand ToggleActivityLogCommand { get; private set; } = null!;
    public ICommand ClearActivityLogsCommand { get; private set; } = null!;
    public ICommand RestartAsAdminCommand { get; private set; } = null!;

    public ICommand OpenFileLocationCommand { get; private set; } = null!;
    public ICommand CopyPathCommand { get; private set; } = null!;
    public ICommand CompressSingleFileCommand { get; private set; } = null!;
    public ICommand DecompressSingleFileCommand { get; private set; } = null!;
    public ICommand ExcludeFromListCommand { get; private set; } = null!;

    public void LogActivity(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        ActivityLogs.Add(logEntry);
    }

    public void SetCategory(FileCategory category) => SelectedCategory = category;

    public void SetCategory(string category)
    {
        if (Enum.TryParse<FileCategory>(category, true, out var parsed))
        {
            SelectedCategory = parsed;
        }
    }

    public void HandleDroppedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (Directory.Exists(path))
        {
            SelectedPath = path;
            LogActivity($"Sürükle-bırak ile klasör seçildi: {path}");
            StatusMessage = $"Klasör seçildi: {path}";
            CurrentActionText = $"Taramak için 'TARA' butonuna tıklayın: {path}";
        }
        else if (File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                SelectedPath = dir;
                LogActivity($"Sürükle-bırak ile dosyanın klasörü seçildi: {dir}");
                StatusMessage = $"Klasör seçildi: {dir}";
                CurrentActionText = $"Taramak için 'TARA' butonuna tıklayın: {dir}";
            }
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
        OnPropertyChanged(nameof(FilteredFilesCount));
        OnPropertyChanged(nameof(FilterCountText));
        OnPropertyChanged(nameof(IsFilterActive));
    }

    private void InitializeAlgorithmOptions()
    {
        AlgorithmOptions.Clear();

        AlgorithmOptions.Add(new AlgorithmOptionViewModel
        {
            Algorithm = CompressionAlgorithm.Lzx,
            Name = "LZX (CompactOS)",
            Subtitle = "Maksimum Tasarruf (Önerilen) | %40-%70 Kazanç",
            Description = "Oyunlar ve Programlar için ideal en yüksek sıkıştırma oranı.",
            RequiresAdmin = true,
            IsRecommended = true
        });

        AlgorithmOptions.Add(new AlgorithmOptionViewModel
        {
            Algorithm = CompressionAlgorithm.Xpress8K,
            Name = "XPRESS 8K",
            Subtitle = "Dengeli | Hızlı sıkıştırma ve açma",
            Description = "Hafif CPU kullanımı ile hızlı erişim gerektiren dosyalar için dengeli profil.",
            RequiresAdmin = true,
            IsRecommended = false
        });

        AlgorithmOptions.Add(new AlgorithmOptionViewModel
        {
            Algorithm = CompressionAlgorithm.Ntfs,
            Name = "NTFS (LZNT1)",
            Subtitle = "Klasik NTFS | Standart kullanıcı hakları",
            Description = "Geleneksel Windows dosya sistemi sıkıştırması. Yönetici yetkisi gerektirmez.",
            RequiresAdmin = false,
            IsRecommended = false
        });
    }

    private void InitializeCommands()
    {
        BrowseFolderCommand = new RelayCommand(ExecuteBrowseFolder, () => !IsBusy);
        QuickSelectDriveCommand = new RelayCommand<string>(ExecuteQuickSelectDrive, _ => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ExecuteScanAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedPath));
        CancelCommand = new RelayCommand(ExecuteCancel, () => IsBusy);
        CompressSelectedCommand = new AsyncRelayCommand(ExecuteCompressSelectedAsync, () => !IsBusy && SelectedFilesCount > 0);
        DecompressSelectedCommand = new AsyncRelayCommand(ExecuteDecompressSelectedAsync, () => !IsBusy && SelectedFilesCount > 0);
        SelectAllCommand = new RelayCommand(() => SetAllItemsSelection(true), () => !IsBusy && Files.Count > 0);
        DeselectAllCommand = new RelayCommand(() => SetAllItemsSelection(false), () => !IsBusy && Files.Count > 0);
        InvertSelectionCommand = new RelayCommand(ExecuteInvertSelection, () => !IsBusy && Files.Count > 0);
        SelectLargeFilesCommand = new RelayCommand(ExecuteSelectLargeFiles, () => !IsBusy && Files.Count > 0);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => !string.IsNullOrEmpty(SearchText));
        SelectCategoryCommand = new RelayCommand<object>(ExecuteSelectCategory);
        ToggleActivityLogCommand = new RelayCommand(() => IsActivityLogVisible = !IsActivityLogVisible);
        ClearActivityLogsCommand = new RelayCommand(() => ActivityLogs.Clear());
        RestartAsAdminCommand = new RelayCommand(ExecuteRestartAsAdmin);

        OpenFileLocationCommand = new RelayCommand<FileItemViewModel>(ExecuteOpenFileLocation);
        CopyPathCommand = new RelayCommand<FileItemViewModel>(ExecuteCopyPath);
        CompressSingleFileCommand = new AsyncRelayCommand<FileItemViewModel>(ExecuteCompressSingleFileAsync, item => !IsBusy && item != null);
        DecompressSingleFileCommand = new AsyncRelayCommand<FileItemViewModel>(ExecuteDecompressSingleFileAsync, item => !IsBusy && item != null);
        ExcludeFromListCommand = new RelayCommand<FileItemViewModel>(ExecuteExcludeFromList, item => !IsBusy && item != null);
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

    private void ExecuteOpenFileLocation(FileItemViewModel? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
            return;

        if (OpenFileLocationHandler != null)
        {
            OpenFileLocationHandler(item.FilePath);
            LogActivity($"Dosya konumu açıldı: {item.FileName}");
            return;
        }

        try
        {
            if (File.Exists(item.FilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.FilePath}\"",
                    UseShellExecute = true
                });
                LogActivity($"Dosya konumu açıldı: {item.FileName}");
            }
        }
        catch (Exception ex)
        {
            LogActivity($"Dosya konumu açılamadı: {ex.Message}");
        }
    }

    private void ExecuteCopyPath(FileItemViewModel? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
            return;

        if (ClipboardHandler != null)
        {
            ClipboardHandler(item.FilePath);
        }
        else
        {
            try
            {
                Clipboard.SetText(item.FilePath);
            }
            catch (Exception ex)
            {
                LogActivity($"Panoya kopyalanamadı: {ex.Message}");
                return;
            }
        }

        LogActivity($"Dosya yolu kopyalandı: {item.FilePath}");
    }

    public async Task ExecuteCompressSingleFileAsync(FileItemViewModel? item)
    {
        if (IsBusy || item == null)
            return;

        IsCompressing = true;
        StatusMessage = $"{item.FileName} sıkıştırılıyor ({SelectedAlgorithm})...";
        CurrentActionText = $"Sıkıştırılıyor: {item.FileName}";
        LogActivity($"Tekil dosya sıkıştırma başlatıldı: {item.FileName} ({SelectedAlgorithm})");

        try
        {
            bool success = await _compressionService.CompressFileAsync(item.Model, SelectedAlgorithm, CancellationToken.None);
            item.Refresh();
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            if (success)
            {
                StatusMessage = $"{item.FileName} başarıyla sıkıştırıldı.";
                CurrentActionText = $"Tasarruf: {ByteSizeConverter.Format(item.BytesSaved)}.";
                LogActivity($"Sıkıştırma tamamlandı: {item.FileName} ({ByteSizeConverter.Format(item.BytesSaved)} tasarruf)");
            }
            else
            {
                StatusMessage = $"{item.FileName} sıkıştırılamadı.";
                LogActivity($"Sıkıştırma başarısız: {item.FileName} - {item.ErrorMessage ?? "Hata"}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
            LogActivity($"Sıkıştırma hatası ({item.FileName}): {ex.Message}");
        }
        finally
        {
            IsCompressing = false;
        }
    }

    public async Task ExecuteDecompressSingleFileAsync(FileItemViewModel? item)
    {
        if (IsBusy || item == null)
            return;

        IsCompressing = true;
        StatusMessage = $"{item.FileName} sıkıştırması kaldırılıyor...";
        CurrentActionText = $"Sıkıştırma kaldırılıyor: {item.FileName}";
        LogActivity($"Tekil dosya sıkıştırma kaldırma: {item.FileName}");

        try
        {
            bool success = await _compressionService.DecompressFileAsync(item.Model, CancellationToken.None);
            item.Refresh();
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            if (success)
            {
                StatusMessage = $"{item.FileName} orijinal boyutuna döndürüldü.";
                CurrentActionText = "Sıkıştırma kaldırıldı.";
                LogActivity($"Sıkıştırma kaldırıldı: {item.FileName}");
            }
            else
            {
                StatusMessage = $"{item.FileName} sıkıştırması kaldırılamadı.";
                LogActivity($"Sıkıştırma kaldırma başarısız: {item.FileName} - {item.ErrorMessage ?? "Hata"}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
            LogActivity($"Sıkıştırma kaldırma hatası ({item.FileName}): {ex.Message}");
        }
        finally
        {
            IsCompressing = false;
        }
    }

    public void ExecuteExcludeFromList(FileItemViewModel? item)
    {
        if (item == null)
            return;

        if (Files.Remove(item))
        {
            TotalFilesFound = Files.Count;
            TotalBytesFound = Files.Sum(f => f.Size);
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            ApplyFilter();
            LogActivity($"Dosya listeden çıkarıldı: {item.FileName}");
            StatusMessage = $"{item.FileName} listeden çıkarıldı.";
        }
    }

    public void ExecuteInvertSelection()
    {
        if (IsBusy || Files.Count == 0)
            return;

        _isUpdatingSelectionAll = true;
        try
        {
            foreach (var file in Files)
            {
                file.IsSelected = !file.IsSelected;
            }
        }
        finally
        {
            _isUpdatingSelectionAll = false;
        }

        UpdateSelectionMetrics();
        LogActivity("Dosya seçimi tersine çevrildi.");
    }

    public void ExecuteSelectLargeFiles()
    {
        if (IsBusy || Files.Count == 0)
            return;

        _isUpdatingSelectionAll = true;
        try
        {
            foreach (var file in Files)
            {
                file.IsSelected = file.Size >= LargeFileThreshold;
            }
        }
        finally
        {
            _isUpdatingSelectionAll = false;
        }

        UpdateSelectionMetrics();
        LogActivity($"Büyük dosyalar (>= 50 MB) seçildi: {SelectedFilesCount} dosya.");
    }

    private void ExecuteBrowseFolder()
    {
        if (FolderPicker != null)
        {
            var chosen = FolderPicker();
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                SelectedPath = chosen;
            }
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Taranacak veya Sıkıştırılacak Klasörü Seçin",
            InitialDirectory = Directory.Exists(SelectedPath) ? SelectedPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedPath = dialog.FolderName;
        }
    }

    private void ExecuteQuickSelectDrive(string? driveParam)
    {
        if (string.IsNullOrWhiteSpace(driveParam))
            return;

        if (driveParam.Equals("USER", StringComparison.OrdinalIgnoreCase))
        {
            SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else
        {
            SelectedPath = driveParam;
        }
    }

    public async Task ExecuteScanAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(SelectedPath))
            return;

        IsScanning = true;
        StatusMessage = "Tarama başlatılıyor...";
        CurrentActionText = $"Klasör taranıyor: {SelectedPath}";
        ProgressPercentage = 0;
        ProgressText = "Taranıyor...";
        LogActivity($"Tarama başlatıldı: {SelectedPath} (Alt klasörler: {IncludeSubdirectories}, Sadece sıkıştırılmamış: {OnlyUncompressed})");

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        var scanOptions = new ScanOptions
        {
            RootPath = SelectedPath,
            IncludeSubdirectories = IncludeSubdirectories,
            OnlyUncompressed = OnlyUncompressed
        };

        var progress = new Progress<ScanProgressReport>(report =>
        {
            StatusMessage = $"Taranıyor: {report.ScannedFilesCount} dosya, {report.MatchedFilesCount} uygun dosya bulundu";
            CurrentActionText = string.IsNullOrWhiteSpace(report.CurrentDirectory)
                ? "Dizinler taranıyor..."
                : $"Taranan dizin: {report.CurrentDirectory}";
        });

        try
        {
            var results = await _fileScanner.ScanAsync(scanOptions, progress, token);

            Files.Clear();

            var sortedList = results.OrderByDescending(f => f.Size).ToList();

            foreach (var item in sortedList)
            {
                var vm = new FileItemViewModel(item, OnItemSelectionChanged)
                {
                    IsSelected = !item.IsCompressed
                };
                Files.Add(vm);
            }

            TotalFilesFound = Files.Count;
            TotalBytesFound = Files.Sum(f => f.Size);
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);

            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            ApplyFilter();

            StatusMessage = $"Tarama tamamlandı: {TotalFilesFound} dosya bulundu ({ByteSizeConverter.Format(TotalBytesFound)}).";
            CurrentActionText = $"Seçili {SelectedFilesCount} dosya ({ByteSizeConverter.Format(SelectedFilesTotalSize)}) sıkıştırma için hazır.";
            ProgressPercentage = 100;
            ProgressText = $"{TotalFilesFound} dosya";
            LogActivity($"Tarama tamamlandı: {TotalFilesFound} dosya ({ByteSizeConverter.Format(TotalBytesFound)})");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Tarama kullanıcı tarafından iptal edildi.";
            CurrentActionText = "Tarama durduruldu.";
            LogActivity("Tarama işlemi kullanıcı tarafından iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Tarama hatası: {ex.Message}";
            CurrentActionText = "Tarama sırasında bir hata oluştu.";
            LogActivity($"Tarama hatası: {ex.Message}");
        }
        finally
        {
            _activeCts?.Dispose();
            _activeCts = null;
            IsScanning = false;
        }
    }

    public async Task ExecuteCompressSelectedAsync()
    {
        if (IsBusy)
            return;

        var selectedItems = Files.Where(f => f.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            StatusMessage = "Lütfen sıkıştırılacak en az bir dosya seçin.";
            return;
        }

        IsCompressing = true;
        StatusMessage = $"{selectedItems.Count} dosya sıkıştırılıyor ({SelectedAlgorithm})...";
        ProgressPercentage = 0;
        ProgressText = $"0 / {selectedItems.Count}";
        LogActivity($"Toplu sıkıştırma başlatıldı: {selectedItems.Count} dosya ({SelectedAlgorithm})");

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        var fileMap = selectedItems.ToDictionary(k => k.FilePath, StringComparer.OrdinalIgnoreCase);

        var progress = new Progress<CompressionProgressReport>(report =>
        {
            ProgressPercentage = report.PercentComplete;
            ProgressText = $"{report.ProcessedFiles} / {report.TotalFiles} (%{report.PercentComplete:0.0})";
            CurrentActionText = $"Sıkıştırılıyor: {Path.GetFileName(report.CurrentFilePath)} ({ByteSizeConverter.Format(report.TotalBytesSaved)} kazanıldı)";

            if (!string.IsNullOrEmpty(report.CurrentFilePath) && fileMap.TryGetValue(report.CurrentFilePath, out var currentVm))
            {
                currentVm.Refresh();
            }

            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
        });

        try
        {
            var targetModels = selectedItems.Select(vm => vm.Model).ToList();
            var summary = await _compressionService.CompressFilesAsync(
                targetModels,
                SelectedAlgorithm,
                maxDegreeOfParallelism: 4,
                progress: progress,
                cancellationToken: token);

            foreach (var item in selectedItems)
            {
                item.Refresh();
            }

            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            StatusMessage = $"Sıkıştırma tamamlandı: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata.";
            CurrentActionText = $"İşlem bitti. Toplam tasarruf: {ByteSizeConverter.Format(summary.TotalBytesSaved)}.";
            ProgressPercentage = 100;
            LogActivity($"Toplu sıkıştırma bitti: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata. Toplam kazanç: {ByteSizeConverter.Format(summary.TotalBytesSaved)}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sıkıştırma işlemi iptal edildi.";
            CurrentActionText = "Kullanıcı işlemi durdurdu.";
            foreach (var item in selectedItems)
            {
                item.Refresh();
            }
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            LogActivity("Sıkıştırma işlemi iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sıkıştırma hatası: {ex.Message}";
            CurrentActionText = "Hata oluştu.";
            LogActivity($"Sıkıştırma sırasında hata: {ex.Message}");
        }
        finally
        {
            _activeCts?.Dispose();
            _activeCts = null;
            IsCompressing = false;
        }
    }

    public async Task ExecuteDecompressSelectedAsync()
    {
        if (IsBusy)
            return;

        var selectedItems = Files.Where(f => f.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            StatusMessage = "Lütfen sıkıştırması kaldırılacak en az bir dosya seçin.";
            return;
        }

        IsCompressing = true;
        StatusMessage = $"{selectedItems.Count} dosyanın sıkıştırması kaldırılıyor...";
        ProgressPercentage = 0;
        ProgressText = $"0 / {selectedItems.Count}";
        LogActivity($"Sıkıştırma kaldırma başlatıldı: {selectedItems.Count} dosya");

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        var fileMap = selectedItems.ToDictionary(k => k.FilePath, StringComparer.OrdinalIgnoreCase);

        var progress = new Progress<CompressionProgressReport>(report =>
        {
            ProgressPercentage = report.PercentComplete;
            ProgressText = $"{report.ProcessedFiles} / {report.TotalFiles} (%{report.PercentComplete:0.0})";
            CurrentActionText = $"Sıkıştırma kaldırılıyor: {Path.GetFileName(report.CurrentFilePath)}";

            if (!string.IsNullOrEmpty(report.CurrentFilePath) && fileMap.TryGetValue(report.CurrentFilePath, out var currentVm))
            {
                currentVm.Refresh();
            }

            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
        });

        try
        {
            var targetModels = selectedItems.Select(vm => vm.Model).ToList();
            var summary = await _compressionService.DecompressFilesAsync(
                targetModels,
                maxDegreeOfParallelism: 4,
                progress: progress,
                cancellationToken: token);

            foreach (var item in selectedItems)
            {
                item.Refresh();
            }

            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();

            StatusMessage = $"Sıkıştırma kaldırma tamamlandı: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata.";
            CurrentActionText = "Dosyalar orijinal boyutlarına döndürüldü.";
            ProgressPercentage = 100;
            LogActivity($"Sıkıştırma kaldırma bitti: {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sıkıştırma kaldırma işlemi iptal edildi.";
            CurrentActionText = "Kullanıcı işlemi durdurdu.";
            foreach (var item in selectedItems)
            {
                item.Refresh();
            }
            TotalBytesSaved = Files.Sum(f => f.BytesSaved);
            UpdateSelectionMetrics();
            UpdateStorageMetrics();
            LogActivity("Sıkıştırma kaldırma iptal edildi.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
            CurrentActionText = "Sıkıştırma kaldırılırken hata oluştu.";
            LogActivity($"Sıkıştırma kaldırma hatası: {ex.Message}");
        }
        finally
        {
            _activeCts?.Dispose();
            _activeCts = null;
            IsCompressing = false;
        }
    }

    private void ExecuteCancel()
    {
        if (IsBusy && _activeCts != null && !_activeCts.IsCancellationRequested)
        {
            StatusMessage = "İşlem iptal ediliyor...";
            CurrentActionText = "Lütfen bekleyin...";
            _activeCts.Cancel();
            LogActivity("İptal isteği gönderildi.");
        }
    }

    private void ExecuteRestartAsAdmin()
    {
        LogActivity("Yönetici olarak yeniden başlatılıyor...");
        _adminService.RestartAsAdmin();
    }

    internal void OnItemSelectionChanged(FileItemViewModel? _ = null)
    {
        UpdateSelectionMetrics();
    }

    public FileItemViewModel CreateFileItem(FileItemInfo model) => new(model, OnItemSelectionChanged);

    private void SetAllItemsSelection(bool select)
    {
        _isUpdatingSelectionAll = true;
        try
        {
            foreach (var file in Files)
            {
                file.IsSelected = select;
            }
        }
        finally
        {
            _isUpdatingSelectionAll = false;
        }

        UpdateSelectionMetrics();
    }

    private void UpdateSelectionMetrics()
    {
        int count = 0;
        long totalSize = 0;
        long totalSaved = 0;

        foreach (var file in Files)
        {
            if (file.IsSelected)
            {
                count++;
                totalSize += file.Size;
                totalSaved += file.BytesSaved;
            }
        }

        SelectedFilesCount = count;
        SelectedFilesTotalSize = totalSize;
        SelectedFilesBytesSaved = totalSaved;

        _isUpdatingSelectionAll = true;
        try
        {
            if (Files.Count == 0)
                IsAllSelected = false;
            else if (count == Files.Count)
                IsAllSelected = true;
            else if (count == 0)
                IsAllSelected = false;
            else
                IsAllSelected = null;
        }
        finally
        {
            _isUpdatingSelectionAll = false;
        }

        RaiseCommandStatesChanged();
    }

    public void UpdateStorageMetrics()
    {
        TotalUncompressedBytes = Files.Sum(f => f.Size);
        TotalCompressedBytes = Files.Sum(f => f.AllocatedSize);
        TotalSavedBytes = Math.Max(0, TotalUncompressedBytes - TotalCompressedBytes);

        SavingsPercentage = TotalUncompressedBytes > 0
            ? Math.Max(0.0, Math.Min(100.0, ((double)TotalSavedBytes / TotalUncompressedBytes) * 100.0))
            : 0.0;

        SavingsPercentageText = TotalSavedBytes > 0
            ? $"-%{SavingsPercentage.ToString("0.0", CultureInfo.InvariantCulture)} Alan Tasarrufu"
            : "%0 Alan Tasarrufu";

        OnPropertyChanged(nameof(CompressedWidthRatio));
        OnPropertyChanged(nameof(SavedWidthRatio));
    }

    private void RaiseCommandStatesChanged()
    {
        (BrowseFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (QuickSelectDriveCommand as RelayCommand<string>)?.RaiseCanExecuteChanged();
        (ScanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CompressSelectedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DecompressSelectedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SelectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeselectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (InvertSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SelectLargeFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CompressSingleFileCommand as AsyncRelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (DecompressSingleFileCommand as AsyncRelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (ExcludeFromListCommand as RelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (OpenFileLocationCommand as RelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (CopyPathCommand as RelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
    }
}
