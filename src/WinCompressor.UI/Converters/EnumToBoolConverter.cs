using System.Globalization;
using System.Windows.Data;

namespace WinCompressor.UI.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        string checkValue = value.ToString() ?? string.Empty;
        string targetValue = parameter.ToString() ?? string.Empty;

        return string.Equals(checkValue, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is not null)
        {
            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, parameter.ToString()!);
            }
            if (targetType == typeof(string))
            {
                return parameter.ToString()!;
            }
        }

        return Binding.DoNothing;
    }
}
