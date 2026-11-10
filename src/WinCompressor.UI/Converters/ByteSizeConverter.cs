using System.Globalization;
using System.Windows.Data;

namespace WinCompressor.UI.Converters;

public class ByteSizeConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return "0 B";

        double bytes;
        try
        {
            bytes = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return "0 B";
        }

        return Format(bytes, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    public static string Format(double bytes, CultureInfo? culture = null)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes <= 0)
            return "0 B";

        var targetCulture = culture ?? CultureInfo.CurrentCulture;
        int order = 0;
        double len = bytes;

        while (len >= 1024 && order < Units.Length - 1)
        {
            order++;
            len /= 1024.0;
        }

        return order == 0
            ? $"{len:0} {Units[order]}"
            : $"{len.ToString("0.00", targetCulture)} {Units[order]}";
    }

    public static string Format(long bytes, CultureInfo? culture = null) => Format((double)bytes, culture);
}
