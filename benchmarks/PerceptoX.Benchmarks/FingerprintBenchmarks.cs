using BenchmarkDotNet.Attributes;
using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class FingerprintBenchmarks
{
    private readonly PerceptualHash64 _perceptual = new();
    private readonly DifferenceHash64 _difference = new();
    private readonly MultiRegionHash576 _multiRegion = new();
    private readonly byte[] _perceptualPixels = new byte[32 * 32];
    private readonly byte[] _differencePixels = new byte[9 * 8];
    private readonly byte[] _multiRegionPixels = new byte[128 * 128];

    [GlobalSetup]
    public void Setup()
    {
        for (int i = 0; i < _perceptualPixels.Length; i++)
        {
            _perceptualPixels[i] = (byte)((i * 17 + i / 32 * 13) & 255);
        }

        for (int i = 0; i < _differencePixels.Length; i++)
        {
            _differencePixels[i] = (byte)((i * 31 + i / 9 * 7) & 255);
        }

        for (int i = 0; i < _multiRegionPixels.Length; i++)
        {
            _multiRegionPixels[i] = (byte)((i * 19 + i / 128 * 23) & 255);
        }
    }

    [Benchmark]
    public Fingerprint PerceptualHash() => _perceptual.Compute(_perceptualPixels);

    [Benchmark]
    public Fingerprint DifferenceHash() => _difference.Compute(_differencePixels);

    [Benchmark]
    public Fingerprint MultiRegionHash() => _multiRegion.Compute(_multiRegionPixels);
}
