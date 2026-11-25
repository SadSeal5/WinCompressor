using WinCompressor.Core.Models;
using WinCompressor.Core.Services;
using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

/// <summary>
/// Faz 4: sürüm etiketinin assembly sürümünden üretilmesi (csproj'ta tek kaynak).
/// CI ve publish profili yerel test edilemez; bu testler XAML'deki etiketin
/// asla bayatlamayacağını güvence altına alır.
/// </summary>
public class Phase4ReleaseTests
{
    private class FakeAdmin : IAdminService
    {
        public bool IsAdministrator() => false;
        public bool RestartAsAdmin(string? arguments = null) => true;
    }

    private class FakeScanner : IFileScannerService
    {
        public Task<IReadOnlyList<FileItemInfo>> ScanAsync(
            ScanOptions options,
            IProgress<ScanProgressReport>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FileItemInfo>>([]);

        public bool IsRootDrive(string path) => false;
    }

    private class FakeCompression : ICompressionService
    {
        public Task<bool> CompressFileAsync(FileItemInfo fileItem, CompressionAlgorithm algorithm, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> DecompressFileAsync(FileItemInfo fileItem, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<CompressionResultSummary> CompressFilesAsync(IEnumerable<FileItemInfo> files, CompressionAlgorithm algorithm, int maxDegreeOfParallelism = 4, IProgress<CompressionProgressReport>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new CompressionResultSummary());
        public Task<CompressionResultSummary> DecompressFilesAsync(IEnumerable<FileItemInfo> files, int maxDegreeOfParallelism = 4, IProgress<CompressionProgressReport>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new CompressionResultSummary());
    }

    [Fact]
    public void AppVersionText_IsDerivedFromAssemblyVersion()
    {
        var vm = new MainViewModel(new FakeScanner(), new FakeCompression(), new FakeAdmin());

        var version = typeof(MainViewModel).Assembly.GetName().Version;
        Assert.NotNull(version);
        Assert.Equal($"v{version!.Major}.{version.Minor} GUI Pro", vm.AppVersionText);
    }

    [Fact]
    public void AssemblyVersion_IsMajorMinorPinned()
    {
        // csproj: AssemblyVersion 2.1.0.0 — XAML etiketi "v2.1 GUI Pro" bunu yansıtır.
        var version = typeof(MainViewModel).Assembly.GetName().Version!;

        Assert.Equal(2, version.Major);
        Assert.Equal(1, version.Minor);
    }
}
