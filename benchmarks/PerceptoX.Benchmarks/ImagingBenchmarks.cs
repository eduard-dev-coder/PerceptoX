using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using PerceptoX.Application.Imaging;
using PerceptoX.Application.Matching;
using PerceptoX.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5)]
public class ImagingBenchmarks : IDisposable
{
    private string? _root;
    private string? _sourcePath;
    private ImageSharpImageProcessor? _processor;
    private readonly ImageSharpFingerprintExtractor _extractor = new();
    private int _version;

    [Params(512, 2048)]
    public int ImageSide { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string parent = Path.Combine(AppContext.BaseDirectory, ".benchmark-artifacts");
        _root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _sourcePath = Path.Combine(_root, "source.png");
        using Image<Rgba32> image = new(ImageSide, ImageSide);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)((x * 3 + y) & 255), (byte)((x + y * 5) & 255), (byte)((x * 7 + y * 2) & 255));
                }
            }
        });
        image.SaveAsPng(_sourcePath);
        _processor = new ImageSharpImageProcessor();
    }

    [Benchmark]
    public ImageFingerprintSet ExtractOnly() => _extractor.Extract(_sourcePath!);

    [Benchmark]
    public ImageProcessingResult ExtractAndGenerateThumbnail()
    {
        ImageProcessingRequest request = new(
            _sourcePath!,
            1,
            Interlocked.Increment(ref _version).ToString(CultureInfo.InvariantCulture),
            Path.Combine(_root!, "thumbs"));
        return _processor!.ProcessAsync(request).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
        string parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".benchmark-artifacts"));
        if (_root is not null)
        {
            string root = Path.GetFullPath(_root);
            if (!root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Benchmark artifact path escaped its dedicated parent.");
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _processor, null)?.Dispose();
        GC.SuppressFinalize(this);
    }
}
