using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PerceptoX.Application.Matching;

namespace PerceptoX.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class SearchBenchmarks
{
    private SimilaritySearchService _service = null!;
    private SearchIndexSnapshot _snapshot = null!;
    private long _queryId;

    [Params(100_000, 500_000, 1_000_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        SearchIndexEntry[] entries = new SearchIndexEntry[Count];
        ulong state = 0x9E3779B97F4A7C15UL;
        for (int index = 0; index < entries.Length; index++)
        {
            state ^= state << 7;
            state ^= state >> 9;
            ulong perceptual = state;
            state ^= state << 8;
            state ^= state >> 11;
            entries[index] = new SearchIndexEntry(index + 1L, perceptual, state,
                1920, 1080, 2_000_000);
        }

        _queryId = entries[entries.Length / 2].ImageId;
        _snapshot = new SearchIndexSnapshot(1, "benchmark", entries);
        _service = new SimilaritySearchService(new LinearHammingSearchIndex());
    }

    [Benchmark]
    public SimilaritySearchResult LinearHammingTop50() =>
        _service.FindSimilarToSelectedImage(_snapshot, _queryId,
            new SimilaritySearchOptions(50, 8, 8));
}
