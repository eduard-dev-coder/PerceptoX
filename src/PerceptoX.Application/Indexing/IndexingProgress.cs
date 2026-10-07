namespace PerceptoX.Application.Indexing;

public sealed record IndexingProgress(long ScanId, int Discovered, int SkippedUnchanged,
    int Processed, int Failed, int Unsupported, int Inaccessible, double ImagesPerSecond, string Stage)
{
    public int Corrupt { get; init; }
    public string? CurrentFileName { get; init; }
    public int? Total { get; init; }
    public int Persisted { get; init; }
    public string Activity { get; init; } = "Library";
}
