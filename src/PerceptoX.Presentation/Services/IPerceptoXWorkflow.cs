using PerceptoX.Presentation.Localization;
namespace PerceptoX.Presentation.Services;

public sealed record WorkspaceConfiguration(
    string LibraryRoot,
    string DatabasePath,
    string ThumbnailCacheRoot)
{
    public IProgress<PerceptoX.Application.Indexing.IndexingProgress>? Progress { get; init; }
}

public sealed record IndexingSummary(
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

public sealed record SimilarImageItem(
    long ImageId,
    string FilePath,
    Uri? ThumbnailUri,
    double ScorePercent,
    int PerceptualDistance,
    int DifferenceDistance,
    int? MultiRegionBestDistance,
    int MultiRegionMatchedRegions)
{
    public string? QueryVersion { get; init; }
    public string? CandidateVersion { get; init; }
    public int RegionMeasurementDistance { get; init; } = 1;
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }
    public DateTime OriginalLastWriteTimeUtc { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public string PreviewPlaceholder => ThumbnailUri is null ? UiText.T("IPerceptoXWorkflow.Text001") : string.Empty;
    public string ScoreLabel => $"{ScorePercent:F2}%";
    public string DistanceLabel => MultiRegionBestDistance is int regional
        ? UiText.T("IPerceptoXWorkflow.Text002", PerceptualDistance, DifferenceDistance, regional, MultiRegionMatchedRegions)
        : UiText.T("IPerceptoXWorkflow.Text003", PerceptualDistance, DifferenceDistance);
    public string AccessibilityLabel =>
        UiText.T("IPerceptoXWorkflow.Text004", Path.GetFileName(FilePath), ScorePercent.ToString("F2", System.Globalization.CultureInfo.CurrentCulture), DistanceLabel);
}

public sealed record SimilarityQuery(
    WorkspaceConfiguration Workspace,
    string SelectedImagePath,
    int TopN,
    int MaxPerceptualDistance,
    int MaxDifferenceDistance,
    int MaxMultiRegionDistance,
    int MinimumMatchedRegions);

public enum BatchMatchStatus
{
    NearIdentical,
    Found,
    Ambiguous,
    NotFound,
    Error
}

public sealed record BatchMatchRequest(
    WorkspaceConfiguration Workspace,
    string QueryRoot,
    int TopCandidates,
    int MaxPerceptualDistance,
    int MaxDifferenceDistance,
    int MaxMultiRegionDistance,
    int MinimumMatchedRegions);

public sealed record BatchMatchItem(
    string QueryPath,
    BatchMatchStatus Status,
    long? MatchedImageId,
    string? MatchedPath,
    Uri? ThumbnailUri,
    double? ScorePercent,
    int CandidateCount,
    int? PerceptualDistance,
    int? DifferenceDistance,
    int? MultiRegionBestDistance,
    string? ErrorMessage)
{
    public Uri? QueryThumbnailUri { get; init; }
    public string QueryPreviewPlaceholder => QueryThumbnailUri is null ? UiText.T("IPerceptoXWorkflow.Text005") : string.Empty;
    public IReadOnlyList<SimilarImageItem> Candidates { get; init; } = [];
    public string QueryName => Path.GetFileName(QueryPath);
    public string MatchedName => MatchedPath is null ? "—" : Path.GetFileName(MatchedPath);
    public string ScoreLabel => ScorePercent is double score ? $"{score:F2}%" : "—";
    public string StatusLabel => Status switch
    {
        BatchMatchStatus.NearIdentical => UiText.T("IPerceptoXWorkflow.Text006"),
        BatchMatchStatus.Found => UiText.T("IPerceptoXWorkflow.Text007"),
        BatchMatchStatus.Ambiguous => UiText.T("IPerceptoXWorkflow.Text008"),
        BatchMatchStatus.NotFound => UiText.T("IPerceptoXWorkflow.Text009"),
        BatchMatchStatus.Error => UiText.T("IPerceptoXWorkflow.Text010"),
        _ => Status.ToString()
    };
}

public sealed record BatchMatchSummary(
    IndexingSummary Indexing,
    int TotalQueries,
    int Found,
    int NearIdentical,
    int Ambiguous,
    int NotFound,
    int Failed,
    IReadOnlyList<BatchMatchItem> Items);

public interface IPerceptoXWorkflow
{
    Task<IndexingSummary> IndexAsync(
        WorkspaceConfiguration workspace,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SimilarImageItem>> FindSimilarAsync(
        SimilarityQuery query,
        CancellationToken cancellationToken);

    Task<BatchMatchSummary> MatchFolderAsync(
        BatchMatchRequest request,
        CancellationToken cancellationToken);
}
