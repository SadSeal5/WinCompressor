using WinCompressor.Core.Models;

namespace WinCompressor.UI.ViewModels;

public class AlgorithmOptionViewModel : ViewModelBase
{
    private CompressionAlgorithm _algorithm;
    private string _name = string.Empty;
    private string _subtitle = string.Empty;
    private string _description = string.Empty;
    private string _subtitleColor = "#00BCF8";
    private bool _requiresAdmin;
    private bool _isRecommended;
    private bool _isSelected;
    private bool _isEnabled = true;

    public CompressionAlgorithm Algorithm
    {
        get => _algorithm;
        init => SetProperty(ref _algorithm, value);
    }

    public string Name
    {
        get => _name;
        init => SetProperty(ref _name, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        init => SetProperty(ref _subtitle, value);
    }

    public string Description
    {
        get => _description;
        init => SetProperty(ref _description, value);
    }

    /// <summary>Hex color used for the subtitle text (e.g. "#00BCF8").</summary>
    public string SubtitleColor
    {
        get => _subtitleColor;
        init => SetProperty(ref _subtitleColor, value);
    }

    public bool RequiresAdmin
    {
        get => _requiresAdmin;
        init => SetProperty(ref _requiresAdmin, value);
    }

    public bool IsRecommended
    {
        get => _isRecommended;
        init => SetProperty(ref _isRecommended, value);
    }

    /// <summary>Two-way bound to the algorithm card RadioButton in the UI.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>False when the current user cannot use this algorithm (admin required).</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}
