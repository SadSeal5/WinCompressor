namespace WinCompressor.UI.ViewModels;

public enum FileCategory
{
    All,
    Executables,
    LargeFiles,
    TextCode,
    DataMedia,

    /// <summary>Files compressed with a CompactOS WOF algorithm (XPRESS / LZX).</summary>
    CompactOs
}
