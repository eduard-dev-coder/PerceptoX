using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, iterationCount: 5)]
public class MultiRegionSearchBenchmarks
{
    private MultiRegionSearchIndexSnapshot _snapshot = null!;

    [Params(100_000, 1_000_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        long[] ids = new long[Count];
        byte[] hashes = new byte[checked(Count * MultiRegionHash576.ByteLength)];
        Random random = new(1776);
        random.NextBytes(hashes);
        for (int index = 0; index < Count; index++)
        {
            ids[index] = index + 1L;
        }

        _snapshot = new MultiRegionSearchIndexSnapshot(1, "benchmark-v1", ids, hashes);
    }

    [Benchmark]
    public IReadOnlyList<MultiRegionSearchMatch> LinearMultiRegionTop50() =>
        MultiRegionSearchService.FindSimilar(
            _snapshot,
            1,
            new MultiRegionSearchOptions(topN: 50, maximumRegionDistance: 1));
}
