namespace PerceptoX.Application.Matching;

public sealed record SimilaritySearchOptions
{
    public SimilaritySearchOptions(int topN = 50, int maxPerceptualDistance = 2,
        int maxDifferenceDistance = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topN, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(topN, 1000);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPerceptualDistance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPerceptualDistance, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDifferenceDistance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDifferenceDistance, 64);
        TopN = topN;
        MaxPerceptualDistance = maxPerceptualDistance;
        MaxDifferenceDistance = maxDifferenceDistance;
    }

    public int TopN { get; }
    public int MaxPerceptualDistance { get; }
    public int MaxDifferenceDistance { get; }

    public static SimilaritySearchOptions Broad(int topN = 50) => new(topN, 64, 64);
}
