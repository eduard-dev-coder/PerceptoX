using PerceptoX.Core.Images;

namespace PerceptoX.Application.Matching;

public sealed record SearchImageDetail(ImageRecord Image, ThumbnailRecord? Thumbnail);

public sealed record SearchReportEntry(
    SearchImageDetail Detail,
    int PerceptualDistance,
    int DifferenceDistance,
    double ScorePercent,
    bool IsQuery,
    int? MultiRegionBestDistance = null,
    int MultiRegionMatchedRegions = 0);

public sealed record SearchReport(
    long ScanId,
    string ProcessingProfileId,
    IReadOnlyList<SearchReportEntry> Entries)
{
    public bool IsCalibrated { get; }
}
