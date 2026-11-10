using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WinCompressor.UI.Converters;

public class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(46, 204, 113));
    private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(52, 152, 219));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(231, 76, 60));
    private static readonly SolidColorBrush CyanBrush = new(Color.FromRgb(0, 188, 248));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(170, 170, 170));

    static StatusToBrushConverter()
    {
        GreenBrush.Freeze();
        BlueBrush.Freeze();
        RedBrush.Freeze();
        CyanBrush.Freeze();
        GrayBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? string.Empty;

        return status.ToUpperInvariant() switch
        {
            "COMPRESSED" or "SIKIŞTIRILDI" => GreenBrush,
            "DECOMPRESSED" or "SIKIŞTIRILMAMIŞ" => BlueBrush,
            "COMPRESSING" or "SIKIŞTIRILIYOR" => CyanBrush,
            "FAILED" or "HATA" => RedBrush,
            _ => GrayBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
