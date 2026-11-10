using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinCompressor.UI.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }
    public Visibility FalseVisibility { get; set; } = Visibility.Collapsed;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolVal = false;
        if (value is bool b)
        {
            boolVal = b;
        }

        bool invert = Inverse;
        if (parameter is string paramStr && bool.TryParse(paramStr, out bool parsedParam))
        {
            invert ^= parsedParam;
        }
        else if (parameter is string pStr && pStr.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            invert = !invert;
        }

        if (invert)
        {
            boolVal = !boolVal;
        }

        return boolVal ? Visibility.Visible : FalseVisibility;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Visibility visibility)
            return false;

        bool isVisible = visibility == Visibility.Visible;
        bool invert = Inverse;
        if (parameter is string pStr && pStr.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            invert = !invert;
        }

        return invert ? !isVisible : isVisible;
    }
}
