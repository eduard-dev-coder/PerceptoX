namespace PerceptoX.Application.Matching;

public readonly record struct SimilarityMatch(
    long ImageId,
    int PerceptualDistance,
    int DifferenceDistance,
    double ScorePercent,
    int? MultiRegionBestDistance = null,
    int MultiRegionMatchedRegions = 0);

public sealed record SimilaritySearchResult(
    long QueryImageId,
    long ScanId,
    string ProcessingProfileId,
    IReadOnlyList<SimilarityMatch> Matches)
{
    public bool IsCalibrated { get; }
}
