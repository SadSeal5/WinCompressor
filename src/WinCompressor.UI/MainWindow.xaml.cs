using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WinCompressor.Core.Services;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly JsonSettingsService? _settingsService;

    /// <summary>
    /// T3-B1 (inceleme turu 3): üretim ctor'u VM'e ayrı bir JsonSettingsService
    /// örneği verip pencere tarafını null bırakıyordu — kapanışta hiçbir ayar
    /// kaydedilmiyor, pencere konumu yüklenmiyordu. Artık TEK örnek hem VM'e hem
    /// pencereye geçilir; tüm tercihler kapanışta bu örnekten kaydedilir.
    /// </summary>
    public static (MainViewModel ViewModel, JsonSettingsService Settings) CreateProductionDependencies()
    {
        var settings = new JsonSettingsService();
        var viewModel = new MainViewModel(
            new FileScannerService(new AlgorithmDetectionService()),
            new CompressionService(),
            new AdminService(),
            new SavingsEstimationService(),
            settings,
            new JsonCompressionHistoryService(),
            new GameLibraryService(),
            new ScheduledTaskService());

        return (viewModel, settings);
    }

    public MainWindow()
    {
        var (viewModel, settings) = CreateProductionDependencies();

        // Zincirleme ctor yerine el ile kurulum: settings örneği yalnız bir kez
        // oluşturulup paylaştırılır (this(...) argüman listesinde out paylaşımı
        // güvenilir olmadığından gövde tercih edildi).
        InitializeComponent();
        DataContext = viewModel;
        _settingsService = settings;

        viewModel.ActivityLogs.CollectionChanged += OnActivityLogsCollectionChanged;

        SourceInitialized += (_, _) => ApplyWindowBounds();
        Closing += OnMainWindowClosing;
    }

    public MainWindow(MainViewModel viewModel, JsonSettingsService? settingsService = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settingsService = settingsService;

        viewModel.ActivityLogs.CollectionChanged += OnActivityLogsCollectionChanged;

        SourceInitialized += (_, _) => ApplyWindowBounds();
        Closing += OnMainWindowClosing;
    }

    // ------------------------------------------------------------------
    // Settings persistence (window bounds + scan preferences)

    /// <summary>Restores the persisted window position/size/maximized state.</summary>
    private void ApplyWindowBounds()
    {
        if (_settingsService is null)
            return;

        var settings = LoadSettingsForWindow();
        if (settings is null || settings.WindowMaximized)
        {
            if (settings is { WindowMaximized: true })
            {
                WindowState = WindowState.Maximized;
            }
            return;
        }

        // NaN means "never persisted": keep the XAML-defined startup state.
        if (double.IsNaN(settings.WindowWidth) || double.IsNaN(settings.WindowHeight))
            return;

        try
        {
            double width = Math.Max(600, Math.Min(settings.WindowWidth, SystemParameters.WorkArea.Width * 1.5));
            double height = Math.Max(400, Math.Min(settings.WindowHeight, SystemParameters.WorkArea.Height * 1.5));

            double left = settings.WindowLeft;
            double top = settings.WindowTop;

            if (!double.IsNaN(left) && !double.IsNaN(top))
            {
                // Keep the window reachable: clamp into (an area around) the work area.
                left = Math.Max(-width / 2, Math.Min(left, SystemParameters.WorkArea.Width - width * 0.3));
                top = Math.Max(0, Math.Min(top, SystemParameters.WorkArea.Height - 60));

                Left = left;
                Top = top;
            }

            Width = width;
            Height = height;
        }
        catch
        {
            // Bad bounds values: keep startup defaults.
        }
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        try
        {
            var settings = vm.CaptureSettings();

            // T4-N1 (inceleme turu 4): Minimized VEYA Maximized durumunda Left/Top/Width/Height
            // ekran/tam-ekran sınırlarını yansıtır — kalıcı olması gereken "geri yükle"
            // boyutu RestoreBounds'tan okunmalı, yoksa maksimize kapatılan oturumda
            // kullanıcı pencereyi restore edince XAML varsayılanına düşer.
            bool boundsFromRestore = WindowState is WindowState.Minimized or WindowState.Maximized;
            settings.WindowLeft = boundsFromRestore ? RestoreBounds.Left : Left;
            settings.WindowTop = boundsFromRestore ? RestoreBounds.Top : Top;
            settings.WindowWidth = boundsFromRestore ? RestoreBounds.Width : Width;
            settings.WindowHeight = boundsFromRestore ? RestoreBounds.Height : Height;
            settings.WindowMaximized = WindowState == WindowState.Maximized;

            _settingsService?.Save(settings);
        }
        catch
        {
            // Saving settings must never block shutdown.
        }
    }

    private Core.Models.AppSettings? LoadSettingsForWindow()
    {
        try
        {
            return _settingsService?.Load();
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Activity log auto-scroll
    // ------------------------------------------------------------------

    private void OnActivityLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogListBox != null && LogListBox.Items.Count > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        }
    }

    // ------------------------------------------------------------------
    // Grid interactions: open file (double-click / Enter), column hide/reset
    // ------------------------------------------------------------------

    private void FilesDataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: FileItemViewModel item } &&
            DataContext is MainViewModel vm)
        {
            vm.OpenFileCommand.Execute(item);
        }
    }

    private void FilesDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            FilesDataGrid.SelectedItem is not FileItemViewModel item ||
            DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.OpenFileCommand.Execute(item);
        e.Handled = true;
    }

    private void HideColumnMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: DataGridColumnHeader header } })
        {
            header.Column.Visibility = Visibility.Collapsed;
        }
    }

    private void ResetColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var column in FilesDataGrid.Columns)
        {
            column.Visibility = Visibility.Visible;
        }
    }

    // ------------------------------------------------------------------
    // Drag & drop target folder handling
    // ------------------------------------------------------------------

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.HandleDroppedPath(files[0]);
                }
            }
        }
    }
}
