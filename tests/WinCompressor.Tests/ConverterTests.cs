using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WinCompressor.Core.Models;
using WinCompressor.UI.Converters;

namespace WinCompressor.Tests;

public class ConverterTests
{
    [Fact]
    public void ByteSizeConverter_ConvertsValuesCorrectly()
    {
        var conv = new ByteSizeConverter();
        var culture = CultureInfo.InvariantCulture;

        // Requirement: 1048576 -> "1.00 MB"
        Assert.Equal("1.00 MB", conv.Convert(1048576L, typeof(string), null, culture));
        Assert.Equal("0 B", conv.Convert(null, typeof(string), null, culture));
        Assert.Equal("0 B", conv.Convert(0L, typeof(string), null, culture));
        Assert.Equal("0 B", conv.Convert(-500L, typeof(string), null, culture));
        Assert.Equal("500 B", conv.Convert(500L, typeof(string), null, culture));
        Assert.Equal("1.00 KB", conv.Convert(1024L, typeof(string), null, culture));
        Assert.Equal("2.50 MB", conv.Convert((long)(2.5 * 1024 * 1024), typeof(string), null, culture));
        Assert.Equal("1.00 GB", conv.Convert(1073741824L, typeof(string), null, culture));
        Assert.Equal("3.25 TB", conv.Convert((long)(3.25 * 1024 * 1024 * 1024 * 1024), typeof(string), null, culture));
    }

    [Fact]
    public void BoolToVisibilityConverter_ConvertsStandardAndInverse()
    {
        var conv = new BoolToVisibilityConverter();
        var culture = CultureInfo.InvariantCulture;

        Assert.Equal(Visibility.Visible, conv.Convert(true, typeof(Visibility), null, culture));
        Assert.Equal(Visibility.Collapsed, conv.Convert(false, typeof(Visibility), null, culture));

        conv.Inverse = true;
        Assert.Equal(Visibility.Collapsed, conv.Convert(true, typeof(Visibility), null, culture));
        Assert.Equal(Visibility.Visible, conv.Convert(false, typeof(Visibility), null, culture));
    }

    [Fact]
    public void EnumToBoolConverter_ConvertsAndConvertsBack()
    {
        var conv = new EnumToBoolConverter();
        var culture = CultureInfo.InvariantCulture;

        Assert.True((bool)conv.Convert(CompressionAlgorithm.Lzx, typeof(bool), CompressionAlgorithm.Lzx, culture));
        Assert.False((bool)conv.Convert(CompressionAlgorithm.Ntfs, typeof(bool), CompressionAlgorithm.Lzx, culture));

        var backResult = conv.ConvertBack(true, typeof(CompressionAlgorithm), CompressionAlgorithm.Xpress8K, culture);
        Assert.Equal(CompressionAlgorithm.Xpress8K, backResult);
    }

    [Fact]
    public void StatusToBrushConverter_ReturnsExpectedBrushes()
    {
        var conv = new StatusToBrushConverter();
        var culture = CultureInfo.InvariantCulture;

        var compressedBrush = (SolidColorBrush)conv.Convert("Compressed", typeof(SolidColorBrush), null, culture);
        Assert.Equal(Color.FromRgb(46, 204, 113), compressedBrush.Color);

        var failedBrush = (SolidColorBrush)conv.Convert("Failed", typeof(SolidColorBrush), null, culture);
        Assert.Equal(Color.FromRgb(231, 76, 60), failedBrush.Color);
    }
}
