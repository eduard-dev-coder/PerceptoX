using PerceptoX.Presentation.Localization;
using PerceptoX.Infrastructure.Exporting;
using PerceptoX.Presentation.Services;
using PerceptoX.Infrastructure.Maintenance;

namespace PerceptoX.WinUI.Services;

public sealed class DesktopBatchResultActions(Func<WorkspaceConfiguration>? workspace = null) : IBatchResultActions
{
    public Task<CopyOutcome> CopyAsync(IReadOnlyList<string> sourcePaths, string destination,
        CancellationToken cancellationToken) => Task.Run(async () =>
    {
        FileCopyResult result = await MatchedFileCopier.CopyAsync(sourcePaths, destination, cancellationToken);
        return new CopyOutcome(result.Copied, result.Errors.Count, result.Cancelled, result.Errors);
    });

    public Task<string> ExportAsync(BatchMatchSummary summary, string destination, bool html,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        WorkspaceConfiguration? configured = workspace?.Invoke();
        using WorkspaceLease? lease = configured is null ? null : WorkspaceLease.Acquire(configured.DatabasePath, configured.ThumbnailCacheRoot);
        string totals = UiText.T("DesktopBatchResultActions.Text001", summary.TotalQueries, summary.Found) +
            UiText.T("DesktopBatchResultActions.Text002", summary.NearIdentical, summary.Ambiguous) +
            UiText.T("DesktopBatchResultActions.Text003", summary.NotFound, summary.Failed);
        BatchReport report = new(totals, summary.Items.Select(item => new BatchReportRow(
            item.QueryPath, item.QueryThumbnailUri?.LocalPath, item.StatusLabel, item.ErrorMessage,
            item.Candidates.Select(candidate => new BatchReportCandidate(candidate.FilePath,
                candidate.ThumbnailUri?.LocalPath, candidate.ScorePercent)).ToArray())).ToArray());
        return BatchReportExporter.Export(report, destination, html, cancellationToken);
    }, cancellationToken);
}
