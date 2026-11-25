using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinCompressor.UI.Converters;

/// <summary>
/// T2-K1 (inceleme turu 2): boş-durum yer tutucusu <c>Files.Count</c> (int) ile
/// bağlanıyordu ama <see cref="BoolToVisibilityConverter"/> yalnız bool tanıyor,
/// bu yüzden yer tutucu listede dosya olsa bile hep görünür kalıyordu. Bu
/// converter sayı tabanlıdır: sayaç 0 ise (ve Inverse değilse) Visible.
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    /// <summary>true ise sayaç &gt; 0 olduğunda Visible döner (0'da değil).</summary>
    public bool Inverse { get; set; }

    public Visibility FalseVisibility { get; set; } = Visibility.Collapsed;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasItems = value switch
        {
            int i => i > 0,
            long l => l > 0,
            _ => false
        };

        bool show = Inverse ? hasItems : !hasItems;
        return show ? Visibility.Visible : FalseVisibility;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
