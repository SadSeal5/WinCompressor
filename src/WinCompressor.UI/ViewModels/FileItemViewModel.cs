using WinCompressor.Core.Models;
using WinCompressor.UI.Converters;

namespace WinCompressor.UI.ViewModels;

public class FileItemViewModel : ViewModelBase
{
    private readonly FileItemInfo _model;
    private bool _isSelected;
    private readonly Action<FileItemViewModel>? _onSelectionChanged;

    public FileItemViewModel(FileItemInfo model, Action<FileItemViewModel>? onSelectionChanged = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _onSelectionChanged = onSelectionChanged;
    }

    public FileItemInfo Model => _model;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onSelectionChanged?.Invoke(this);
            }
        }
    }

    public string FilePath => _model.FilePath;
    public string FileName => _model.FileName;
    public string Extension => _model.Extension;
    public long Size => _model.Size;
    public long AllocatedSize => _model.AllocatedSize;
    public long BytesSaved => _model.BytesSaved;
    public double CompressionRatio => _model.CompressionRatio;
    public double SavingsPercentage => _model.SavingsPercentage;
    public string Status => _model.Status;
    public string? ErrorMessage => _model.ErrorMessage;
    public bool IsCompressed => _model.IsCompressed;
    public CompressionAlgorithm CurrentAlgorithm => _model.CurrentAlgorithm;

    public string SavingsText => BytesSaved > 0
        ? $"{ByteSizeConverter.Format(BytesSaved)} (%{SavingsPercentage:0.0})"
        : "-";

    public void Refresh()
    {
        OnPropertyChanged(nameof(AllocatedSize));
        OnPropertyChanged(nameof(BytesSaved));
        OnPropertyChanged(nameof(CompressionRatio));
        OnPropertyChanged(nameof(SavingsPercentage));
        OnPropertyChanged(nameof(SavingsText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(IsCompressed));
        OnPropertyChanged(nameof(CurrentAlgorithm));
    }
}
