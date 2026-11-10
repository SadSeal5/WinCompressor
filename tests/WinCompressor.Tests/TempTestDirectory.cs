namespace WinCompressor.Tests;

public sealed class TempTestDirectory : IDisposable
{
    public string Path { get; }

    public TempTestDirectory(string prefix = "WinCompressor_Test_")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string CreateFile(string relativeName, int sizeInBytes, byte fillByte = (byte)'A')
    {
        var filePath = System.IO.Path.Combine(Path, relativeName);
        var dir = System.IO.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[Math.Min(65536, sizeInBytes)];
        Array.Fill(buffer, fillByte);
        int remaining = sizeInBytes;
        while (remaining > 0)
        {
            int toWrite = Math.Min(buffer.Length, remaining);
            fs.Write(buffer, 0, toWrite);
            remaining -= toWrite;
        }

        return filePath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failure in temporary location
        }
    }
}
