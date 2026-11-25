using System.IO;
using System.Text.Json;
using WinCompressor.Core.Models;
using WinCompressor.Core.Services;

namespace WinCompressor.Core.Cli;

/// <summary>
/// Faz 5.2: betiklendirilebilir CLI. Komutlar:
///
///   WinCompressor cli scan       &lt;path&gt; [seçenekler]
///   WinCompressor cli compress   &lt;path&gt; --algorithm &lt;ad&gt; [seçenekler] [--dry-run]
///   WinCompressor cli decompress &lt;path&gt; [seçenekler]
///
/// Ortak seçenekler:
///   --no-subdirs           alt klasörleri tarama
///   --include-compressed   sıkıştırılmış dosyaları da listele / işleme dahil et
///   --appdata              AppData klasörlerini dahil et
///   --min-size &lt;bayt&gt;      minimum dosya boyutu (varsayılan 4096)
///   --json                 makineye okunur JSON çıktı (scan)
///   --dry-run              compress için tahmini tasarruf raporu (dosyalara dokunmaz)
///
/// Algoritma adları: ntfs, lzx, xpress4k, xpress8k, xpress16k
/// Çıkış kodları: 0 başarı, 1 kullanım hatası, 2 kısmi/komple işlem hatası.
/// </summary>
public sealed class CliDispatcher
{
    private readonly IFileScannerService _scanner;
    private readonly ICompressionService _compression;
    private readonly ISavingsEstimationService? _estimator;
    private readonly IAdminService? _adminService;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CliDispatcher(
        IFileScannerService scanner,
        ICompressionService compression,
        ISavingsEstimationService? estimator = null,
        TextWriter? output = null,
        TextWriter? error = null,
        IAdminService? adminService = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _compression = compression ?? throw new ArgumentNullException(nameof(compression));
        _estimator = estimator;
        _output = output ?? Console.Out;
        _error = error ?? Console.Error;
        _adminService = adminService;
    }

    /// <summary>Üretim yapılandırması: gerçek tarama (WOF tespiti ile) + gerçek sıkıştırma.</summary>
    public static CliDispatcher CreateDefault(TextWriter? output = null, TextWriter? error = null) =>
        new(
            new FileScannerService(new AlgorithmDetectionService()),
            new CompressionService(),
            new SavingsEstimationService(),
            output,
            error,
            new AdminService());

    public async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "scan" => await RunScanAsync(args[1..]),
                "compress" => await RunCompressAsync(args[1..]),
                "decompress" => await RunDecompressAsync(args[1..]),
                _ => UsageError($"Bilinmeyen komut: '{args[0]}'")
            };
        }
        catch (OperationCanceledException)
        {
            _error.WriteLine("İşlem iptal edildi.");
            return 2;
        }
        catch (Exception ex)
        {
            _error.WriteLine($"Hata: {ex.Message}");
            return 2;
        }
    }

    // ------------------------------------------------------------------
    // Komutlar
    // ------------------------------------------------------------------

    private async Task<int> RunScanAsync(string[] args)
    {
        var options = ParseScanOptions(args, requirePath: true);
        if (options is null)
            return 1;

        var results = await _scanner.ScanAsync(options.Scan);
        PrintScanResults(results, options.AsJson);

        long totalSize = results.Sum(r => r.Size);
        long totalAllocated = results.Sum(r => r.AllocatedSize);
        _output.WriteLine($"# {results.Count} dosya, {totalSize:N0} bayt, diskte {totalAllocated:N0} bayt");

        return 0;
    }

    private async Task<int> RunCompressAsync(string[] args)
    {
        var parsed = ParseCompressionOptions(args, requireAlgorithm: true);
        if (parsed is null)
            return 1;

        // T4-N2 (inceleme turu 4): admin gerektiren algoritma + standart kullanıcı
        // → dosyaları tek tek compact.exe hatasıyla düşürmek yerine hızlı, tek
        // ve açık bir hata (UI'daki EnsureAlgorithmAllowed muadili).
        if (_adminService is not null
            && parsed.Algorithm!.Value.RequiresAdmin()
            && !_adminService.IsAdministrator())
        {
            _error.WriteLine($"Hata: '{parsed.Algorithm}' algoritması yönetici hakları gerektirir. Komutu yönetici olarak çalıştırın.");
            return 1;
        }

        var results = await _scanner.ScanAsync(parsed.Scan);
        var targets = results.Where(r => !r.IsCompressed).ToList();

        if (targets.Count == 0)
        {
            _output.WriteLine("# Sıkıştırılacak uygun dosya yok.");
            return 0;
        }

        if (parsed.DryRun)
        {
            long total = 0;
            foreach (var item in targets)
            {
                long saved = _estimator is null
                    ? 0
                    : await _estimator.EstimateSavedBytesAsync(item.FilePath, item.Size, parsed.Algorithm!.Value);
                total += saved;
                PrintFileLine(item, note: $"tahmini -{saved:N0}");
            }

            _output.WriteLine($"# {targets.Count} dosya için tahmini tasarruf: {total:N0} bayt ({parsed.Algorithm})");
            return 0;
        }

        var summary = await _compression.CompressFilesAsync(
            targets,
            parsed.Algorithm!.Value,
            maxDegreeOfParallelism: Environment.ProcessorCount);

        foreach (var item in summary.Items)
        {
            PrintFileLine(item, item.Status == "Compressed" ? $"tasarruf {item.BytesSaved:N0}" : $"HATA: {item.ErrorMessage}");
        }

        _output.WriteLine($"# {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata, {summary.TotalBytesSaved:N0} bayt kazanıldı");
        return summary.FailedCount > 0 ? 2 : 0;
    }

    private async Task<int> RunDecompressAsync(string[] args)
    {
        var parsed = ParseCompressionOptions(args, requireAlgorithm: false);
        if (parsed is null)
            return 1;

        // Sıkıştırılmış dosyaları bul: tarama filtresi kapalı + yalnız IsCompressed.
        var scanOptions = new ScanOptions
        {
            RootPath = parsed.Scan.RootPath,
            IncludeSubdirectories = parsed.Scan.IncludeSubdirectories,
            OnlyUncompressed = false,
            IncludeAppData = parsed.Scan.IncludeAppData,
            MinFileSize = parsed.Scan.MinFileSize
        };
        var results = await _scanner.ScanAsync(scanOptions);
        var targets = results.Where(r => r.IsCompressed).ToList();

        if (targets.Count == 0)
        {
            _output.WriteLine("# Sıkıştırması kaldırılacak dosya yok.");
            return 0;
        }

        var summary = await _compression.DecompressFilesAsync(
            targets,
            maxDegreeOfParallelism: Environment.ProcessorCount);

        foreach (var item in summary.Items)
        {
            PrintFileLine(item, item.Status == "Decompressed" ? "açıldı" : $"HATA: {item.ErrorMessage}");
        }

        _output.WriteLine($"# {summary.SuccessfulCount} başarılı, {summary.FailedCount} hata");
        return summary.FailedCount > 0 ? 2 : 0;
    }

    // ------------------------------------------------------------------
    // Argüman ayrıştırma
    // ------------------------------------------------------------------

    private sealed record ParsedScanOptions(ScanOptions Scan, bool AsJson);

    private sealed record ParsedCompressionOptions(ScanOptions Scan, CompressionAlgorithm? Algorithm, bool DryRun);

    private ParsedScanOptions? ParseScanOptions(string[] args, bool requirePath)
    {
        string? path = null;
        bool noSubdirs = false;
        bool includeCompressed = false;
        bool includeAppData = false;
        long minSize = 4096;
        bool asJson = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--no-subdirs":
                    noSubdirs = true;
                    break;
                case "--include-compressed":
                    includeCompressed = true;
                    break;
                case "--appdata":
                    includeAppData = true;
                    break;
                case "--json":
                    asJson = true;
                    break;
                case "--min-size":
                    if (i + 1 >= args.Length || !long.TryParse(args[++i], out long parsedMin))
                        return null;
                    minSize = parsedMin;
                    break;
                default:
                    if (arg.StartsWith("--"))
                        return null;
                    if (path is not null)
                        return null;
                    path = arg;
                    break;
            }
        }

        if (requirePath && (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) && !File.Exists(path)))
        {
            _error.WriteLine("Geçerli bir yol belirtin: scan <path>");
            return null;
        }

        return new ParsedScanOptions(
            new ScanOptions
            {
                RootPath = path ?? string.Empty,
                IncludeSubdirectories = !noSubdirs,
                OnlyUncompressed = !includeCompressed,
                IncludeAppData = includeAppData,
                MinFileSize = minSize
            },
            asJson);
    }

    private ParsedCompressionOptions? ParseCompressionOptions(string[] args, bool requireAlgorithm)
    {
        string? algorithmName = null;
        bool dryRun = false;

        // Son Positional argüman yoldur; diğerleri bayrak.
        var positional = new List<string>();
        bool noSubdirs = false;
        bool includeCompressed = false;
        bool includeAppData = false;
        long minSize = 4096;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--algorithm":
                    if (i + 1 >= args.Length)
                        return null;
                    algorithmName = args[++i];
                    break;
                case "--no-subdirs":
                    noSubdirs = true;
                    break;
                case "--include-compressed":
                    includeCompressed = true;
                    break;
                case "--appdata":
                    includeAppData = true;
                    break;
                case "--min-size":
                    if (i + 1 >= args.Length || !long.TryParse(args[++i], out long parsedMin))
                        return null;
                    minSize = parsedMin;
                    break;
                default:
                    if (args[i].StartsWith("--"))
                        return null;
                    positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count != 1 || string.IsNullOrWhiteSpace(positional[0]))
        {
            _error.WriteLine("Geçerli bir yol belirtin (tek yol bağımsız değişkeni).");
            return null;
        }

        string path = positional[0];
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            _error.WriteLine($"Yol bulunamadı: {path}");
            return null;
        }

        CompressionAlgorithm? algorithm = null;
        if (algorithmName is not null)
        {
            algorithm = ParseAlgorithmName(algorithmName);
            if (algorithm is null)
            {
                _error.WriteLine($"Bilinmeyen algoritma: '{algorithmName}' (ntfs, lzx, xpress4k, xpress8k, xpress16k)");
                return null;
            }
        }
        else if (requireAlgorithm)
        {
            _error.WriteLine("--algorithm zorunlu: ntfs, lzx, xpress4k, xpress8k, xpress16k");
            return null;
        }

        return new ParsedCompressionOptions(
            new ScanOptions
            {
                RootPath = path,
                IncludeSubdirectories = !noSubdirs,
                OnlyUncompressed = !includeCompressed,
                IncludeAppData = includeAppData,
                MinFileSize = minSize
            },
            algorithm,
            dryRun);
    }

    public static CompressionAlgorithm? ParseAlgorithmName(string name) =>
        name.Trim().ToLowerInvariant() switch
        {
            "ntfs" => CompressionAlgorithm.Ntfs,
            "lzx" => CompressionAlgorithm.Lzx,
            "xpress4k" => CompressionAlgorithm.Xpress4K,
            "xpress8k" => CompressionAlgorithm.Xpress8K,
            "xpress16k" => CompressionAlgorithm.Xpress16K,
            _ => null
        };

    private static bool IsHelp(string arg) =>
        arg is "--help" or "-h" or "/?" or "help";

    private int UsageError(string message)
    {
        _error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private void PrintUsage()
    {
        _output.WriteLine("""
            Kullanım:
              cli scan <path> [--no-subdirs] [--include-compressed] [--appdata] [--min-size <bayt>] [--json]
              cli compress <path> --algorithm <ntfs|lzx|xpress4k|xpress8k|xpress16k> [--dry-run] [tarama seçenekleri]
              cli decompress <path> [tarama seçenekleri]
            """);
    }

    // ------------------------------------------------------------------
    // Çıktı biçimleri
    // ------------------------------------------------------------------

    private void PrintScanResults(IReadOnlyList<FileItemInfo> results, bool asJson)
    {
        if (asJson)
        {
            var payload = results.Select(r => new
            {
                path = r.FilePath,
                size = r.Size,
                allocated = r.AllocatedSize,
                algorithm = r.CurrentAlgorithm.ToString(),
                compressed = r.IsCompressed
            });
            _output.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        foreach (var item in results)
        {
            PrintFileLine(item);
        }
    }

    private void PrintFileLine(FileItemInfo item, string note = "") =>
        _output.WriteLine($"{item.FilePath}\t{item.Size}\t{item.AllocatedSize}\t{item.CurrentAlgorithm}\t{item.IsCompressed}\t{note}");
}
