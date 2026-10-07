using PerceptoX.Application.Matching;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Persistence;
using PerceptoX.Infrastructure.Maintenance;
using PerceptoX.Infrastructure.Exporting;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.Localization;

namespace PerceptoX.WinUI.Services;

public sealed class DesktopGroupingWorkflow(IPerceptoXWorkflow indexer, OptionalMagickDecoder? decoder = null) : ISimilarityGroupingWorkflow
{
    public async Task<GroupingAnalysisResult> AnalyzeGroupsAsync(GroupingAnalysisRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(request.MinimumScore) || request.MinimumScore < 97 || request.MinimumScore > 100)
            throw new ArgumentOutOfRangeException(nameof(request));
        string[] roots = request.Roots.Select(ManagedPaths.Normalize).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(root => !request.Roots.Any(other => !string.Equals(ManagedPaths.Normalize(other), root, StringComparison.OrdinalIgnoreCase)
                && ManagedPaths.Contains(ManagedPaths.Normalize(other), root))).ToArray();
        if (roots.Length == 0) throw new ArgumentException("Select at least one folder.", nameof(request));
        int indexingErrors = 0;
        foreach (string root in roots)
        {
            token.ThrowIfCancellationRequested(); ManagedPaths.RejectLinks(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            var summary = await indexer.IndexAsync(request.Workspace with { LibraryRoot = root }, token);
            indexingErrors += summary.Failed + summary.Unsupported;
            if (!summary.DiscoveryComplete) throw new IOException(UiText.T("Grouping.IncompleteDiscovery"));
        }
        return await Task.Run(() =>
        {
            using WorkspaceLease lease = WorkspaceLease.Acquire(request.Workspace.DatabasePath, request.Workspace.ThumbnailCacheRoot);
            using ImageSharpImageProcessor processor = new(modernDecoder: decoder);
            SqliteIndexReader reader = new(request.Workspace.DatabasePath);
            var snapshot = reader.LoadGroupingSnapshot(request.EntireIndex ? null : roots, processor.ProcessingProfileId, token: token);
            using GroupingImageVerifier verifier = new(request.Workspace.DatabasePath, request.Workspace.ThumbnailCacheRoot, token, processor);
            var groups = SimilarityGroupingService.Build(snapshot.Fingerprints, request.MinimumScore,
                verify: verifier.Verify, progress: request.AnalysisProgress, cancellationToken: token);
            var run = new SqliteSimilarityStore(request.Workspace.DatabasePath).Publish(snapshot, groups,
                request.MinimumScore, verifier.Rejected, verifier.Failed + indexingErrors, token);
            return ToResult(run);
        }, token);
    }

    public Task<IReadOnlyList<GroupedImageItem>> LoadGroupPageAsync(WorkspaceConfiguration workspace,
        GroupingAnalysisResult run, int offset, int? groupNumber, CancellationToken token)
        => Task.Run<IReadOnlyList<GroupedImageItem>>(() =>
        {
            token.ThrowIfCancellationRequested();
            using var lease = WorkspaceLease.Acquire(workspace.DatabasePath, workspace.ThumbnailCacheRoot);
            return new SqliteSimilarityStore(workspace.DatabasePath).ReadPage(ToRun(run), offset, 50, groupNumber, groupNumber is null)
                .Select(item => new GroupedImageItem(item.GroupNumber, item.ImageId, item.IsRepresentative, item.Score,
                    item.BinaryIdentical, item.FilePath, item.Width, item.Height, item.FileSize, item.LastWriteTicks,
                    ThumbnailUri(workspace.ThumbnailCacheRoot, item.ThumbnailPath), item.GroupSize)).ToArray();
        }, token);

    public Task<CopyOutcome> CopyGroupedAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
        GroupingSelection selection, string destination, CancellationToken token) => Task.Run(async () =>
        {
            using var lease = WorkspaceLease.Acquire(workspace.DatabasePath, workspace.ThumbnailCacheRoot);
            ValidateDestination(workspace, run, destination);
            var store = new SqliteSimilarityStore(workspace.DatabasePath);
            var sources = Enumerate(store, ToRun(run), token).Where(item => selection.Includes(item.ImageId, item.IsRepresentative))
                .Select(item => new CopySource(item.FilePath, item.FileSize, item.LastWriteTicks));
            var copied = await MatchedFileCopier.CopyUniqueVerifiedAsync(sources, destination, token).ConfigureAwait(false);
            return new CopyOutcome(copied.Copied, copied.Errors.Count, copied.Cancelled, copied.Errors);
        }, token);

    public Task<string> ExportGroupsAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
        GroupingSelection selection, string destination, bool html, CancellationToken token) => Task.Run(() =>
        {
            using var lease = WorkspaceLease.Acquire(workspace.DatabasePath, workspace.ThumbnailCacheRoot);
            ValidateDestination(workspace, run, destination);
            var store = new SqliteSimilarityStore(workspace.DatabasePath);
            return GroupingReportExporter.Export(Enumerate(store, ToRun(run), token), destination, workspace.ThumbnailCacheRoot,
                UiText.T("Grouping.ReportSummary", run.Images, run.Groups, run.Members, run.MinimumScore, run.Failed, run.Rejected)
                + UiText.T("Grouping.ExcludedProfiles", run.ExcludedImages)
                + " · " + run.RunId + " · " + string.Join("; ", run.Roots.Select(root => root.Path + " @" + root.ScanId)),
                html, selection.Includes, new(UiText.Language, UiText.T("Grouping.Title"), UiText.T("Grouping.Representative"),
                    UiText.T("Grouping.SelectionLabel", ""), UiText.T("Grouping.BinaryIdentical"), UiText.T("Grouping.CloseVariant"),
                    UiText.T("Common.On"), UiText.T("Common.Off")), token);
        }, token);

    private static IEnumerable<GroupingStoredMember> Enumerate(SqliteSimilarityStore store, GroupingRun run, CancellationToken token)
        => store.EnumerateMembers(run, token);

    private static void ValidateDestination(WorkspaceConfiguration workspace, GroupingAnalysisResult run, string destination)
    {
        ManagedPaths.RejectLinks(destination);
        foreach (string root in run.Roots.Select(r => r.Path).Append(workspace.ThumbnailCacheRoot).Append(Path.GetDirectoryName(workspace.DatabasePath)!))
            if (ManagedPaths.Contains(root, destination) || ManagedPaths.Contains(destination, root))
                throw new IOException(UiText.T("Grouping.InvalidDestination"));
    }

    private static Uri? ThumbnailUri(string cache, string? relative)
    {
        if (relative is null) return null;
        string path = Path.GetFullPath(Path.Combine(cache, relative));
        if (!ManagedPaths.Contains(cache, path)) return null;
        ManagedPaths.RejectLinks(path);
        return File.Exists(path) ? new Uri(path) : null;
    }
    private static GroupingAnalysisResult ToResult(GroupingRun run) => new(run.Id, run.ImageCount, run.GroupCount,
        run.MemberCount, run.Threshold, run.Rejected, run.Failed, run.Roots) { ExcludedImages = run.ExcludedImages };
    private static GroupingRun ToRun(GroupingAnalysisResult run) => new(run.RunId, run.Images, run.Groups, run.Members,
        run.MinimumScore, run.Rejected, run.Failed, run.Roots) { ExcludedImages = run.ExcludedImages };
}
