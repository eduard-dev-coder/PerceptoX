namespace PerceptoX.Application.Indexing;

public sealed record IndexingResult(
    long ScanId,
    int Discovered,
    int SkippedUnchanged,
    int Processed,
    int Failed,
    int Deactivated)
{
    public int Unsupported { get; init; }
    public int Corrupt { get; init; }
    public int Inaccessible { get; init; }
    public bool DiscoveryComplete { get; init; } = true;
}
