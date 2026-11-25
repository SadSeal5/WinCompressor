using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.Converters;

namespace WinCompressor.UI.ViewModels;

/// <summary>
/// Ana görünüm modeli — fazlalık önlemek için konu bazlı partial dosyalara bölünmüştür:
/// Scan (tarama/akış), Compression (sıkıştırma işlemleri), Filtering (arama/kategori),
/// FileItemCommands (dosya başına komutlar), Activity (günlük), Settings (kalıcılık).
/// Public API tüm testlerde koruma altındadır.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IFileScannerService _fileScanner;
    private readonly ICompressionService _compressionService;
    private readonly IAdminService _adminService;
    private readonly ISavingsEstimationService _estimationService;
    private readonly ISettingsService? _settingsService;
    private readonly ICompressionHistoryService? _historyService;
    private readonly IGameLibraryService? _gameLibraryService;
    private readonly IScheduledTaskService? _scheduledTaskService;

    public const long LargeFileThreshold = 50L * 1024L * 1024L; // 50 MB

    /// <summary>Adaptive compression parallelism (clamped to 2..8).</summary>
    private static readonly int CompressionParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8);

    private string _selectedPath = string.Empty;
    private bool _includeSubdirectories = true;
    private bool _onlyUncompressed = true;
    private bool _includeAppData;
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
    private bool _isSyncingAlgorithmSelection;
    private int _filteredFilesCount;
    private bool _canRestoreLastOperation;

    /// <summary>Deduplication for streaming scan result batches (case-insensitive paths).</summary>
    private readonly HashSet<string> _scannedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// UI sürüm etiketi (ör. "v2.1 GUI Pro") — csproj'taki AssemblyVersion'dan
    /// türetilir; XAML'de tek kaynak olarak bu bağlanır.
    /// </summary>
    public string AppVersionText { get; }

    public Func<string?>? FolderPicker { get; set; }
    public Action<string>? OpenFileLocationHandler { get; set; }
    public Action<string>? ClipboardHandler { get; set; }
    public Func<string?>? ExportLogsPathPicker { get; set; }

    /// <summary>
    /// Test seam for opening files; when null the shell open is attempted directly.
    /// </summary>
    public Action<string>? OpenFileHandler { get; set; }

    public MainViewModel()
        : this(
            new FileScannerService(new AlgorithmDetectionService()),
            new CompressionService(),
            new AdminService(),
            new SavingsEstimationService())
    {
    }

    public MainViewModel(
        IFileScannerService fileScanner,
        ICompressionService compressionService,
        IAdminService adminService,
        ISavingsEstimationService? estimationService = null,
        ISettingsService? settingsService = null,
        ICompressionHistoryService? historyService = null,
        IGameLibraryService? gameLibraryService = null,
        IScheduledTaskService? scheduledTaskService = null)
    {
        _fileScanner = fileScanner ?? throw new ArgumentNullException(nameof(fileScanner));
        _compressionService = compressionService ?? throw new ArgumentNullException(nameof(compressionService));
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
        _estimationService = estimationService ?? new SavingsEstimationService();
        _settingsService = settingsService;
        _historyService = historyService;
        _gameLibraryService = gameLibraryService;
        _scheduledTaskService = scheduledTaskService;

        var assemblyVersion = typeof(MainViewModel).Assembly.GetName().Version;
        AppVersionText = assemblyVersion is null
            ? "GUI Pro"
            : $"v{assemblyVersion.Major}.{assemblyVersion.Minor} GUI Pro";

        Files = new ObservableCollection<FileItemViewModel>();
        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.Filter = FilterFileItem;
        FilesView.SortDescriptions.Clear();
        FilesView.SortDescriptions.Add(new SortDescription(nameof(FileItemViewModel.Size), ListSortDirection.Descending));

        AlgorithmOptions = new ObservableCollection<AlgorithmOptionViewModel>();
        ActivityLogs = new ObservableCollection<ActivityLogEntry>();
        GameLibraries = new ObservableCollection<GameLibraryInfo>();

        _isAdministrator = _adminService.IsAdministrator();
        _selectedAlgorithm = _isAdministrator ? CompressionAlgorithm.Lzx : CompressionAlgorithm.Ntfs;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _selectedPath = Directory.Exists(userProfile) ? userProfile : "C:\\";

        InitializeAlgorithmOptions();
        InitializeCommands();

        // Persistence: restore the last session's preferences when a settings
        // service is provided (the parameterless ctor intentionally stays clean
        // for tests).
        if (_settingsService is not null)
        {
            ApplyLoadedSettings(_settingsService.Load());
        }

        // Faz 5: kurulu oyun kütüphaneleri + geri alınabilir işlem durumu.
        LoadGameLibraries();
        RefreshRestoreCapability();

        LogActivity("WinCompressor başlatıldı. Sistem hazır.");
    }

    public ICollectionView FilesView { get; }
    public ObservableCollection<FileItemViewModel> Files { get; }
    public ObservableCollection<AlgorithmOptionViewModel> AlgorithmOptions { get; }
    public ObservableCollection<ActivityLogEntry> ActivityLogs { get; }

    /// <summary>Kurulu oyun kütüphaneleri (Steam/Epic/...); boşsa butonlar görünmez.</summary>
    public ObservableCollection<GameLibraryInfo> GameLibraries { get; }

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
        set
        {
            if (SetProperty(ref _selectedAlgorithm, value))
            {
                OnPropertyChanged(nameof(CanUseSelectedAlgorithm));
                SyncAlgorithmOptionSelection(value);
            }
        }
    }

    /// <summary>
    /// True when the current user may compress with the selected algorithm.
    /// CompactOS WOF algorithms (LZX / XPRESS) require administrator privileges.
    /// </summary>
    public bool CanUseSelectedAlgorithm => !SelectedAlgorithm.RequiresAdmin() || _isAdministrator;

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
    public ICommand SimulateSelectedCommand { get; private set; } = null!;
    public ICommand SelectAllCommand { get; private set; } = null!;
    public ICommand DeselectAllCommand { get; private set; } = null!;
    public ICommand InvertSelectionCommand { get; private set; } = null!;
    public ICommand SelectLargeFilesCommand { get; private set; } = null!;
    public ICommand ClearSearchCommand { get; private set; } = null!;
    public ICommand SelectCategoryCommand { get; private set; } = null!;
    public ICommand ToggleActivityLogCommand { get; private set; } = null!;
    public ICommand ClearActivityLogsCommand { get; private set; } = null!;
    public ICommand ExportActivityLogsCommand { get; private set; } = null!;
    public ICommand RestartAsAdminCommand { get; private set; } = null!;

    public ICommand OpenFileLocationCommand { get; private set; } = null!;
    public ICommand OpenFileCommand { get; private set; } = null!;
    public ICommand CopyPathCommand { get; private set; } = null!;
    public ICommand CompressSingleFileCommand { get; private set; } = null!;
    public ICommand DecompressSingleFileCommand { get; private set; } = null!;
    public ICommand ExcludeFromListCommand { get; private set; } = null!;

    /// <summary>Faz 5.4: sıkıştırma sonrası tek tuşla eski haline döndürme.</summary>
    public ICommand RestoreLastOperationCommand { get; private set; } = null!;

    /// <summary>Faz 5.1: seçili klasör için haftalık sıkıştırma görevi oluşturma.</summary>
    public ICommand ScheduleWeeklyTaskCommand { get; private set; } = null!;

    /// <summary>Faz 5.5: oyun kütüphanesi klasörünü tarama yolu olarak seçme.</summary>
    public ICommand SelectGameLibraryCommand { get; private set; } = null!;

    /// <summary>Son batch geri alınabilir mi (geçmiş servis yoksa her zaman false).</summary>
    public bool CanRestoreLastOperation
    {
        get => _canRestoreLastOperation;
        private set
        {
            if (SetProperty(ref _canRestoreLastOperation, value))
            {
                RaiseCommandStatesChanged();
            }
        }
    }

    private void InitializeCommands()
    {
        BrowseFolderCommand = new RelayCommand(ExecuteBrowseFolder, () => !IsBusy);
        QuickSelectDriveCommand = new RelayCommand<string>(ExecuteQuickSelectDrive, _ => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ExecuteScanAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedPath));
        CancelCommand = new RelayCommand(ExecuteCancel, () => IsBusy);
        CompressSelectedCommand = new AsyncRelayCommand(ExecuteCompressSelectedAsync, () => !IsBusy && SelectedFilesCount > 0);
        DecompressSelectedCommand = new AsyncRelayCommand(ExecuteDecompressSelectedAsync, () => !IsBusy && SelectedFilesCount > 0);
        SimulateSelectedCommand = new AsyncRelayCommand(ExecuteSimulateSelectedAsync, () => !IsBusy && SelectedFilesCount > 0);
        SelectAllCommand = new RelayCommand(() => SetAllItemsSelection(true), () => !IsBusy && Files.Count > 0);
        DeselectAllCommand = new RelayCommand(() => SetAllItemsSelection(false), () => !IsBusy && Files.Count > 0);
        InvertSelectionCommand = new RelayCommand(ExecuteInvertSelection, () => !IsBusy && Files.Count > 0);
        SelectLargeFilesCommand = new RelayCommand(ExecuteSelectLargeFiles, () => !IsBusy && Files.Count > 0);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => !string.IsNullOrEmpty(SearchText));
        SelectCategoryCommand = new RelayCommand<object>(ExecuteSelectCategory);
        ToggleActivityLogCommand = new RelayCommand(() => IsActivityLogVisible = !IsActivityLogVisible);
        ClearActivityLogsCommand = new RelayCommand(() => ActivityLogs.Clear());
        ExportActivityLogsCommand = new RelayCommand(ExecuteExportActivityLogs, () => ActivityLogs.Count > 0);
        RestartAsAdminCommand = new RelayCommand(ExecuteRestartAsAdmin);

        OpenFileLocationCommand = new RelayCommand<FileItemViewModel>(ExecuteOpenFileLocation);
        OpenFileCommand = new RelayCommand<FileItemViewModel>(ExecuteOpenFile, item => item != null);
        CopyPathCommand = new RelayCommand<FileItemViewModel>(ExecuteCopyPath);
        CompressSingleFileCommand = new AsyncRelayCommand<FileItemViewModel>(ExecuteCompressSingleFileAsync, item => !IsBusy && item != null);
        DecompressSingleFileCommand = new AsyncRelayCommand<FileItemViewModel>(ExecuteDecompressSingleFileAsync, item => !IsBusy && item != null);
        ExcludeFromListCommand = new RelayCommand<FileItemViewModel>(ExecuteExcludeFromList, item => !IsBusy && item != null);

        RestoreLastOperationCommand = new AsyncRelayCommand(
            ExecuteRestoreLastOperationAsync, () => !IsBusy && CanRestoreLastOperation);
        ScheduleWeeklyTaskCommand = new RelayCommand(ExecuteScheduleWeeklyTask, () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedPath));
        SelectGameLibraryCommand = new RelayCommand<string>(ExecuteSelectGameLibrary);
    }

    private void InitializeAlgorithmOptions()
    {
        AlgorithmOptions.Clear();

        AddAlgorithmOption(new AlgorithmOptionViewModel
        {
            Algorithm = CompressionAlgorithm.Lzx,
            Name = "LZX (CompactOS)",
            Subtitle = "Maksimum Tasarruf | %40-%70 Kazanç",
            Description = "Oyunlar, programlar ve arşiv dosyaları için ideal en yüksek sıkıştırma oranı.",
            SubtitleColor = "#00BCF8",
            RequiresAdmin = true,
            IsRecommended = true
        });

        AddAlgorithmOption(new AlgorithmOptionViewModel
        {
            Algorithm = CompressionAlgorithm.Xpress4K,
            Name = "XPRESS 4K",
            Subtitle = "En Hızlı | Minimum CPU",
            Description = "En düşük CPU yükü ile en hızlı erişim gereken dosyalar için.",
            SubtitleColor = "#F39C12",
            RequiresAdmin = true,
            IsRecommended = false
        });

        AddAlgorithmOption(new AlgorithmOptionViewModel
        {
            Algorithm = CompressionAlgorithm.Xpress8K,
            Name = "XPRESS 8K",
            Subtitle = "Dengeli | Hızlı sıkıştırma ve açma",
            Description = "Hafif CPU kullanımı ile hızlı erişim gerektiren dosyalar için dengeli profil.",
            SubtitleColor = "#F39C12",
            RequiresAdmin = true,
            IsRecommended = false
        });

        AddAlgorithmOption(new AlgorithmOptionViewModel
        {
            Algorithm = CompressionAlgorithm.Xpress16K,
            Name = "XPRESS 16K",
            Subtitle = "Yüksek Oran | Dengeli hız",
            Description = "XPRESS ailesinin en yüksek oranına sahip üyesi; 4K/8K'dan daha iyi sıkıştırır.",
            SubtitleColor = "#F39C12",
            RequiresAdmin = true,
            IsRecommended = false
        });

        AddAlgorithmOption(new AlgorithmOptionViewModel
        {
            Algorithm = CompressionAlgorithm.Ntfs,
            Name = "NTFS (LZNT1)",
            Subtitle = "Klasik NTFS | Standart kullanıcı hakları",
            Description = "Geleneksel Windows dosya sistemi sıkıştırması. Yönetici yetkisi gerektirmez.",
            SubtitleColor = "#AAAAAA",
            RequiresAdmin = false,
            IsRecommended = false
        });

        SyncAlgorithmOptionSelection(_selectedAlgorithm);
    }

    private void AddAlgorithmOption(AlgorithmOptionViewModel option)
    {
        // Admin-only algorithms are disabled for standard users so the card
        // cannot even be picked, instead of failing per-file at compact.exe time.
        option.IsEnabled = !option.RequiresAdmin || _isAdministrator;

        option.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(AlgorithmOptionViewModel.IsSelected) &&
                !_isSyncingAlgorithmSelection &&
                sender is AlgorithmOptionViewModel { IsSelected: true } selected)
            {
                SelectedAlgorithm = selected.Algorithm;
            }
        };

        AlgorithmOptions.Add(option);
    }

    /// <summary>
    /// Keeps the algorithm card RadioButtons in sync with the selected algorithm
    /// without triggering selection-changed feedback loops.
    /// </summary>
    private void SyncAlgorithmOptionSelection(CompressionAlgorithm selected)
    {
        _isSyncingAlgorithmSelection = true;
        try
        {
            foreach (var option in AlgorithmOptions)
            {
                option.IsSelected = option.Algorithm == selected;
            }
        }
        finally
        {
            _isSyncingAlgorithmSelection = false;
        }
    }

    /// <summary>
    /// T3-B1 test erişimi: üretimde MainWindow ile paylaşılan ayar servisi.
    /// </summary>
    public ISettingsService? SettingsService => _settingsService;

    internal void OnItemSelectionChanged(FileItemViewModel? _ = null)    {
        // T2-K3 (inceleme turu 2): toplu seçim işlemleri (tümünü seç, ters çevir,
        // büyük dosyaları seç) ve tarama akışı sırasında her madde için tam liste
        // metrik taraması yapılması O(n²) üretiyordu; bu yollar bayrakla
        // bastırılır ve işlemin sonunda TEK güncelleme yapılır.
        if (_isUpdatingSelectionAll)
            return;

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
        (SimulateSelectedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SelectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeselectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (InvertSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SelectLargeFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CompressSingleFileCommand as AsyncRelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (DecompressSingleFileCommand as AsyncRelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (ExcludeFromListCommand as RelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (OpenFileLocationCommand as RelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (OpenFileCommand as RelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (CopyPathCommand as RelayCommand<FileItemViewModel>)?.RaiseCanExecuteChanged();
        (ExportActivityLogsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RestoreLastOperationCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ScheduleWeeklyTaskCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
