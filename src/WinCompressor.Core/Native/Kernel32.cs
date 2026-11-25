using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinCompressor.Core.Native;

public static class Kernel32
{
    private const uint InvalidFileSize = 0xFFFFFFFF;

    // --- WOF external backing probe (documented Windows 8.1+ API) ---
    // FSCTL_GET_EXTERNAL_BACKING = CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 196, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint FsctlGetExternalBacking = 0x00090000 | (196 << 2);

    private const uint FileReadAttributes = 0x0080;
    private const uint OpenExisting = 3;
    private const uint FileShareReadWriteDelete = 0x0007; // READ | WRITE | DELETE
    private const uint WofCurrentVersion = 1;

    /// <summary>WOF_PROVIDER_WIM</summary>
    public const uint WofProviderWim = 1;

    /// <summary>WOF_PROVIDER_FILE — used by compact.exe /exe (CompactOS).</summary>
    public const uint WofProviderFile = 2;

    /// <summary>
    /// WOF_EXTERNAL_INFO followed by FILE_PROVIDER_EXTERNAL_INFO_V1, as returned
    /// by FSCTL_GET_EXTERNAL_BACKING for WOF_PROVIDER_FILE-backed files.
    /// </summary>
    public readonly record struct WofExternalBacking(
        uint Provider,
        uint FileVersion,
        uint FileAlgorithm,
        uint Flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        [Out] byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>
    /// Returns the WOF external backing info of a file, or null when the file is
    /// not WOF-backed (plain, NTFS-compressed or sparse files).
    /// Does not require administrator rights and does not spawn any process.
    /// </summary>
    public static WofExternalBacking? GetWofExternalBacking(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        using var handle = CreateFileW(
            path,
            FileReadAttributes,
            FileShareReadWriteDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return null;

        // Input: WOF_EXTERNAL_INFO { Version = WOF_CURRENT_VERSION }.
        var input = new byte[8];
        BitConverter.GetBytes(WofCurrentVersion).CopyTo(input, 0);

        // Output: WOF_EXTERNAL_INFO + FILE_PROVIDER_EXTERNAL_INFO_V1.
        var output = new byte[20];

        if (!DeviceIoControl(
                handle,
                FsctlGetExternalBacking,
                input,
                (uint)input.Length,
                output,
                (uint)output.Length,
                out uint returned,
                IntPtr.Zero) ||
            returned < 12)
        {
            return null;
        }

        uint provider = BitConverter.ToUInt32(output, 4);

        if (provider != WofProviderFile)
            return new WofExternalBacking(provider, 0, 0, 0);

        if (returned < 16)
            return new WofExternalBacking(provider, BitConverter.ToUInt32(output, 8), 0, 0);

        return new WofExternalBacking(
            provider,
            BitConverter.ToUInt32(output, 8),
            BitConverter.ToUInt32(output, 12),
            returned >= 20 ? BitConverter.ToUInt32(output, 16) : 0);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetCompressedFileSizeW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
        out uint lpFileSizeHigh);

    public static long GetDiskAllocatedSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return -1;

        // SetLastError=true ile MSDN kalıbı: dönüş INVALID_FILE_SIZE olduğunda
        // GetLastWin32Error ancak bu noktada anlamlıdır. Başarılı bir çağrıda
        // last-error tanımsızdır; bu yüzden yalnız INVALID_FILE_SIZE dalında
        // okunur ve error==0 ise değerin gerçek (low==0xFFFFFFFF'lik meşru
        // 4GB sınırı boyutu) olduğu kabul edilir.
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

    /// <summary>
    /// Returns the 8.3 short path form of the given file, or null when a short
    /// path is unavailable (8dot3 name creation disabled on the volume).
    /// compact.exe fails with "Out of memory" on paths beyond MAX_PATH even
    /// with the \\?\ prefix, so short paths are the only reliable workaround.
    /// </summary>
    public static string? TryGetShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var buffer = new StringBuilder(2048);
        uint result = GetShortPathNameW(path, buffer, (uint)buffer.Capacity);
        return result > 0 && result < buffer.Capacity ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(
        string lpszLongPath,
        StringBuilder lpszShortPath,
        uint cchBuffer);

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
