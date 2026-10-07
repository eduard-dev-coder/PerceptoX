using BenchmarkDotNet.Attributes;
using PerceptoX.Application.Matching;

namespace PerceptoX.Benchmarks;

[MemoryDiagnoser]
public class GroupingBenchmarks
{
    private SearchIndexSnapshot _snapshot = null!;
    [Params(100_000, 500_000, 1_000_000)] public int Count { get; set; }
    [GlobalSetup]
    public void Setup()
    {
        Random random = new(1091);
        SearchIndexEntry[] entries = new SearchIndexEntry[Count];
        for (int i = 0; i < entries.Length; i++)
        {
            ulong p = (ulong)random.NextInt64(), d = (ulong)random.NextInt64();
            if (i % 5 == 1) { p = entries[i - 1].PerceptualHash; d = entries[i - 1].DifferenceHash; }
            entries[i] = new(i + 1, p, d, 1920, 1080, 100_000);
        }
        _snapshot = new(1, "synthetic-grouping-only", entries);
    }
    // Exercise both candidate indices, but no decoding/SHA/SQLite in this synthetic benchmark.
    [Benchmark] public IReadOnlyList<SimilarityGroup> PackedGrouping() => SimilarityGroupingService.Build(_snapshot,
        verify: static (_, _) => new(true, false, 0.01));
}
