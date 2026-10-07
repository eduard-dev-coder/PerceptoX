namespace PerceptoX.Application.Matching;

public sealed record GroupingRootGeneration(long RootId, long ScanId, string Path);
public sealed record GroupingIndexSnapshot(SearchIndexSnapshot Fingerprints,
    IReadOnlyList<GroupingRootGeneration> Roots)
{
    public int ExcludedImages { get; init; }
}
