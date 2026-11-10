using WinCompressor.Core.Models;

namespace WinCompressor.UI.ViewModels;

public class AlgorithmOptionViewModel : ViewModelBase
{
    private CompressionAlgorithm _algorithm;
    private string _name = string.Empty;
    private string _subtitle = string.Empty;
    private string _description = string.Empty;
    private bool _requiresAdmin;
    private bool _isRecommended;

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
}
