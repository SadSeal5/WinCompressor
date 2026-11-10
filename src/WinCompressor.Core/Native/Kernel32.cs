using System.IO;
using System.Runtime.InteropServices;

namespace WinCompressor.Core.Native;

public static class Kernel32
{
    private const uint InvalidFileSize = 0xFFFFFFFF;

    [DllImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetCompressedFileSizeW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
        out uint lpFileSizeHigh);

    public static long GetDiskAllocatedSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return -1;

        uint low = GetCompressedFileSizeW(path, out uint high);
        if (low == InvalidFileSize)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                try
                {
                    var fi = new FileInfo(path);
                    return fi.Exists ? fi.Length : -1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        return ((long)high << 32) | low;
    }

    public static FileAttributes? GetAttributesSafe(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch
        {
            return null;
        }
    }

    public static bool HasAttribute(FileAttributes attributes, FileAttributes flag) => (attributes & flag) != 0;

    public static bool IsArchive(FileAttributes attributes) => HasAttribute(attributes, FileAttributes.Archive);

    public static bool IsCompressed(FileAttributes attributes) => HasAttribute(attributes, FileAttributes.Compressed);

    public static bool IsReparsePoint(FileAttributes attributes) => HasAttribute(attributes, FileAttributes.ReparsePoint);
}
