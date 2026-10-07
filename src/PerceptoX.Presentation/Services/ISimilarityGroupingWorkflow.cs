using PerceptoX.Application.Matching;

namespace PerceptoX.Presentation.Services;

public sealed record GroupingAnalysisRequest(WorkspaceConfiguration Workspace, IReadOnlyList<string> Roots,
    bool EntireIndex, double MinimumScore, IProgress<int>? AnalysisProgress = null);
public sealed record GroupingAnalysisResult(string RunId, int Images, int Groups, int Members,
    double MinimumScore, int Rejected, int Failed, IReadOnlyList<GroupingRootGeneration> Roots)
{
    public int ExcludedImages { get; init; }
}
public sealed record GroupedImageItem(int GroupNumber, long ImageId, bool IsRepresentative, double Score,
    bool BinaryIdentical, string FilePath, int Width, int Height, long FileSize, long LastWriteTicks,
    Uri? ThumbnailUri, int GroupSize)
{
    public string FileName => Path.GetFileName(FilePath);
    public string Metadata => string.Format(System.Globalization.CultureInfo.GetCultureInfo(Localization.UiText.Language),
        "{0} × {1} · {2:F1} KB", Width, Height, FileSize / 1024d);
    public string ScoreLabel => Score.ToString("F2", System.Globalization.CultureInfo.GetCultureInfo(Localization.UiText.Language)) + "%";
}

public sealed record GroupingSelection(bool Inverted, IReadOnlySet<long> ToggledIds)
{
    public bool Includes(long id, bool representative) => representative ^ Inverted ^ ToggledIds.Contains(id);
}

public interface ISimilarityGroupingWorkflow
{
    Task<GroupingAnalysisResult> AnalyzeGroupsAsync(GroupingAnalysisRequest request, CancellationToken token);
    Task<IReadOnlyList<GroupedImageItem>> LoadGroupPageAsync(WorkspaceConfiguration workspace,
        GroupingAnalysisResult run, int offset, int? groupNumber, CancellationToken token);
    Task<CopyOutcome> CopyGroupedAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
        GroupingSelection selection, string destination, CancellationToken token);
    Task<string> ExportGroupsAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
        GroupingSelection selection, string destination, bool html, CancellationToken token);
}
