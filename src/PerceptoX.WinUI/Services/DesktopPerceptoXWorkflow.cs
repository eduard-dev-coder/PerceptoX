using PerceptoX.Presentation.Localization;
using PerceptoX.Application.Indexing;
using PerceptoX.Application.Imaging;
using PerceptoX.Application.Matching;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Indexing;
using PerceptoX.Infrastructure.Persistence;
using PerceptoX.Presentation.Services;
using PerceptoX.Infrastructure.Maintenance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PerceptoX.Infrastructure.Diagnostics;
using System.Diagnostics;

namespace PerceptoX.WinUI.Services;

public sealed class DesktopPerceptoXWorkflow(ILoggerFactory? loggerFactory = null, OptionalMagickDecoder? modernDecoder = null) : IPerceptoXWorkflow
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<DesktopPerceptoXWorkflow>();
    private readonly OptionalMagickDecoder? _modernDecoder = modernDecoder;
    private bool SupportsModern(string path) => _modernDecoder?.SupportsExtension(Path.GetExtension(path)) == true;
    private ImageIndexer CreateIndexer(ImageSharpImageProcessor processor, WorkspaceConfiguration workspace) =>
        new(processor, logger: _loggerFactory.CreateLogger<ImageIndexer>(), progress: workspace.Progress,
            discovery: (root, issue) => ImageFileDiscovery.EnumerateSupportedImages(root, issue, SupportsModern));
    public Task<IndexingSummary> IndexAsync(WorkspaceConfiguration workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureOutputDirectories(workspace);
            using ImageSharpImageProcessor processor = new(modernDecoder: _modernDecoder);
            ImageIndexer indexer = CreateIndexer(processor, workspace);
            IndexingRequest request = new(workspace.LibraryRoot, workspace.DatabasePath,
                workspace.ThumbnailCacheRoot, processor.ProcessingProfileId);
            IndexingResult result = await indexer.IndexAsync(request, cancellationToken).ConfigureAwait(false);
            return new IndexingSummary(result.ScanId, result.Discovered, result.SkippedUnchanged,
                result.Processed, result.Failed, result.Deactivated)
                { Unsupported = result.Unsupported, Corrupt = result.Corrupt, Inaccessible = result.Inaccessible, DiscoveryComplete = result.DiscoveryComplete };
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SimilarImageItem>> FindSimilarAsync(SimilarityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.Run<IReadOnlyList<SimilarImageItem>>(async () =>
        {
            Guid job = Guid.NewGuid();
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                var matches = await FindSimilarAsyncCore(query, cancellationToken).ConfigureAwait(false);
                if (_logger.IsEnabled(LogLevel.Information)) WorkflowDiagnostics.SearchCompleted(_logger, job,
                    Path.GetFileName(query.SelectedImagePath), matches.Count, watch.Elapsed.TotalMilliseconds);
                return matches;
            }
            catch (Exception exception)
            {
                if (_logger.IsEnabled(LogLevel.Warning)) WorkflowDiagnostics.Stopped(_logger, job, exception.GetType().Name, watch.Elapsed.TotalMilliseconds);
                throw;
            }
        }, cancellationToken);
    }

    public Task<BatchMatchSummary> MatchFolderAsync(
        BatchMatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(async () =>
        {
            Guid job = Guid.NewGuid();
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                var result = await MatchFolderCoreAsync(request, cancellationToken).ConfigureAwait(false);
                if (_logger.IsEnabled(LogLevel.Information)) WorkflowDiagnostics.BatchCompleted(_logger, job,
                    result.TotalQueries, result.Found + result.NearIdentical, result.Ambiguous, result.Failed, watch.Elapsed.TotalMilliseconds);
                return result;
            }
            catch (Exception exception)
            {
                if (_logger.IsEnabled(LogLevel.Warning)) WorkflowDiagnostics.Stopped(_logger, job, exception.GetType().Name, watch.Elapsed.TotalMilliseconds);
                throw;
            }
        }, cancellationToken);
    }

    private async Task<BatchMatchSummary> MatchFolderCoreAsync(
        BatchMatchRequest request,
        CancellationToken cancellationToken)
    {
        ValidateSeparateRoots(request.QueryRoot, request.Workspace.LibraryRoot);
        EnsureOutputDirectories(request.Workspace);

        string processingProfileId;
        IndexingResult indexingResult;
        using (ImageSharpImageProcessor processor = new(modernDecoder: _modernDecoder))
        {
            processingProfileId = processor.ProcessingProfileId;
            IndexingRequest indexingRequest = new(
                request.Workspace.LibraryRoot,
                request.Workspace.DatabasePath,
                request.Workspace.ThumbnailCacheRoot,
                processingProfileId);
            indexingResult = await CreateIndexer(processor, request.Workspace)
                .IndexAsync(indexingRequest, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using WorkspaceLease lease = WorkspaceLease.Acquire(request.Workspace.DatabasePath, request.Workspace.ThumbnailCacheRoot);
        IndexingSummary indexing = new(indexingResult.ScanId, indexingResult.Discovered,
            indexingResult.SkippedUnchanged, indexingResult.Processed,
            indexingResult.Failed, indexingResult.Deactivated)
            { Unsupported = indexingResult.Unsupported, Corrupt = indexingResult.Corrupt, Inaccessible = indexingResult.Inaccessible, DiscoveryComplete = indexingResult.DiscoveryComplete };
        SqliteIndexReader reader = new(request.Workspace.DatabasePath);
        try { request.Workspace.Progress?.Report(new IndexingProgress(indexingResult.ScanId, 0, 0, 0, 0, 0, 0, 0, "Loading") { Activity = "Queries" }); }
        catch (Exception error) { if (_logger.IsEnabled(LogLevel.Warning)) WorkflowDiagnostics.Stopped(_logger, Guid.Empty, error.GetType().Name, 0); }
        SearchIndexSnapshot snapshot = reader.LoadSnapshot(
            request.Workspace.LibraryRoot, processingProfileId);
        MultiRegionSearchIndexSnapshot regional = reader.LoadMultiRegionSnapshot(
            request.Workspace.LibraryRoot, processingProfileId);
        SimilaritySearchOptions options = new(
            request.TopCandidates,
            request.MaxPerceptualDistance,
            request.MaxDifferenceDistance);
        MultiRegionSearchOptions regionalOptions = new(
            Math.Min(request.TopCandidates * 4, 4000),
            request.MaxMultiRegionDistance,
            request.MinimumMatchedRegions);
        SimilaritySearchService search = new();
        using ImageSharpImageProcessor queryProcessor = new(modernDecoder: _modernDecoder);
        string queryCache = Path.Combine(request.Workspace.ThumbnailCacheRoot, "queries");
        List<PendingBatchMatch> pending = [];
        HashSet<long> matchedIds = [];
        int queryDiscovered = 0, queryProcessed = 0, queryFailed = 0, queryDiscoveryComplete = 0;
        string queryStage = "Discovery";
        string? queryName = null;
        Stopwatch queryWatch = Stopwatch.StartNew();
        void ReportQueries()
        {
            try
            {
                request.Workspace.Progress?.Report(new IndexingProgress(indexingResult.ScanId,
                    Volatile.Read(ref queryDiscovered), 0, Volatile.Read(ref queryProcessed), Volatile.Read(ref queryFailed), 0, 0,
                    Volatile.Read(ref queryProcessed) / Math.Max(0.001, queryWatch.Elapsed.TotalSeconds), Volatile.Read(ref queryStage))
                    { Activity = "Queries", CurrentFileName = Volatile.Read(ref queryName),
                        Total = Volatile.Read(ref queryDiscoveryComplete) != 0 ? Volatile.Read(ref queryDiscovered) : null });
            }
            catch (Exception error)
            {
                // A progress observer must not change publication or matching semantics.
                if (_logger.IsEnabled(LogLevel.Warning)) WorkflowDiagnostics.Stopped(_logger, Guid.Empty, error.GetType().Name, queryWatch.Elapsed.TotalMilliseconds);
            }
        }
        using Timer? queryProgressTimer = request.Workspace.Progress is null ? null : new Timer(_ => ReportQueries(), null, 250, 250);
        ReportQueries();

        foreach (string queryPath in ImageFileDiscovery.EnumerateSupportedImages(request.QueryRoot, additionalFormat: SupportsModern))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref queryDiscovered);
            Volatile.Write(ref queryName, Path.GetFileName(queryPath));
            Volatile.Write(ref queryStage, "Processing");
            try
            {
                FileInfo queryFile = new(queryPath);
                string version = $"{Path.GetFullPath(queryPath)}|{queryFile.Length}|{queryFile.LastWriteTimeUtc.Ticks}";
                ImageProcessingResult processed = await queryProcessor.ProcessAsync(
                    new ImageProcessingRequest(queryPath, 1, version, queryCache), cancellationToken)
                    .ConfigureAwait(false);
                ImageFingerprintSet fingerprints = processed.Fingerprints;
                Volatile.Write(ref queryStage, "Search");
                SimilaritySearchResult result = search.FindSimilarToExternalImage(
                    snapshot,
                    regional,
                    fingerprints,
                    new FileInfo(queryPath).Length,
                    options,
                    regionalOptions,
                    cancellationToken);
                pending.Add(new PendingBatchMatch(queryPath, result, null,
                    new Uri(Path.Combine(queryCache, processed.Thumbnail.RelativePath)), version,
                    request.MaxMultiRegionDistance));
                foreach (SimilarityMatch match in result.Matches)
                {
                    matchedIds.Add(match.ImageId);
                }
                Interlocked.Increment(ref queryProcessed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                pending.Add(new PendingBatchMatch(queryPath, null, exception.Message));
                Interlocked.Increment(ref queryFailed);
            }
        }

        Volatile.Write(ref queryDiscoveryComplete, 1);
        Volatile.Write(ref queryStage, "Results");
        Volatile.Write(ref queryName, null);
        if (queryProgressTimer is not null) await queryProgressTimer.DisposeAsync().ConfigureAwait(false);
        ReportQueries();
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyDictionary<long, SearchImageDetail> details = reader.LoadDetails(matchedIds);
        await EnsureThumbnailsAsync(details.Values, request.Workspace.ThumbnailCacheRoot, queryProcessor, cancellationToken).ConfigureAwait(false);
        List<BatchMatchItem> items = new(pending.Count);
        foreach (PendingBatchMatch entry in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(CreateBatchItem(entry, request.Workspace.ThumbnailCacheRoot, details));
        }

        return new BatchMatchSummary(
            indexing,
            items.Count,
            items.Count(item => item.Status == BatchMatchStatus.Found),
            items.Count(item => item.Status == BatchMatchStatus.NearIdentical),
            items.Count(item => item.Status == BatchMatchStatus.Ambiguous),
            items.Count(item => item.Status == BatchMatchStatus.NotFound),
            items.Count(item => item.Status == BatchMatchStatus.Error),
            items);
    }

    private static BatchMatchItem CreateBatchItem(
        PendingBatchMatch entry,
        string thumbnailCacheRoot,
        IReadOnlyDictionary<long, SearchImageDetail> details)
    {
        if (entry.ErrorMessage is not null)
        {
            return new BatchMatchItem(entry.QueryPath, BatchMatchStatus.Error, null, null, null,
                null, 0, null, null, null, entry.ErrorMessage);
        }

        IReadOnlyList<SimilarityMatch> matches = entry.Result!.Matches;
        if (matches.Count == 0)
        {
            return new BatchMatchItem(entry.QueryPath, BatchMatchStatus.NotFound, null, null, null,
                null, 0, null, null, null, null) { QueryThumbnailUri = entry.ThumbnailUri };
        }

        SimilarityMatch best = matches[0];
        if (!details.TryGetValue(best.ImageId, out SearchImageDetail? detail) || !detail.Image.IsActive)
        {
            return new BatchMatchItem(entry.QueryPath, BatchMatchStatus.Error, best.ImageId, null, null,
                best.ScorePercent, matches.Count, best.PerceptualDistance, best.DifferenceDistance,
                best.MultiRegionBestDistance, UiText.T("DesktopPerceptoXWorkflow.Text001"));
        }

        bool nearIdentical = best.PerceptualDistance <= 2 && best.DifferenceDistance <= 2 &&
            best.ScorePercent >= 95d;
        bool ambiguous = matches.Count > 1 && best.ScorePercent - matches[1].ScorePercent < 3d;
        BatchMatchStatus status = nearIdentical
            ? BatchMatchStatus.NearIdentical
            : ambiguous
                ? BatchMatchStatus.Ambiguous
                : BatchMatchStatus.Found;
        return new BatchMatchItem(
            entry.QueryPath,
            status,
            best.ImageId,
            detail.Image.FilePath,
            CreateThumbnailUri(thumbnailCacheRoot, detail),
            best.ScorePercent,
            matches.Count,
            best.PerceptualDistance,
            best.DifferenceDistance,
            best.MultiRegionBestDistance,
            null)
        {
            QueryThumbnailUri = entry.ThumbnailUri,
            Candidates = matches.Where(match => details.TryGetValue(match.ImageId, out var candidate) && candidate.Image.IsActive)
                .Select(match => new SimilarImageItem(match.ImageId, details[match.ImageId].Image.FilePath,
                    CreateThumbnailUri(thumbnailCacheRoot, details[match.ImageId]), match.ScorePercent,
                    match.PerceptualDistance, match.DifferenceDistance, match.MultiRegionBestDistance,
                    match.MultiRegionMatchedRegions)
                {
                    QueryVersion = entry.QueryVersion,
                    CandidateVersion = $"{Path.GetFullPath(details[match.ImageId].Image.FilePath)}|{details[match.ImageId].Image.FileSize}|{details[match.ImageId].Image.LastWriteTimeUtc.Ticks}",
                    RegionMeasurementDistance = entry.RegionMeasurementDistance,
                    OriginalWidth = details[match.ImageId].Image.Width,
                    OriginalHeight = details[match.ImageId].Image.Height,
                    OriginalLastWriteTimeUtc = details[match.ImageId].Image.LastWriteTimeUtc.UtcDateTime
                }).ToArray()
        };
    }

    private static void ValidateSeparateRoots(string queryRoot, string libraryRoot)
    {
        string query = Path.TrimEndingDirectorySeparator(Path.GetFullPath(queryRoot));
        string library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        if (IsSameOrNested(query, library) || IsSameOrNested(library, query))
        {
            throw new ArgumentException(
                UiText.T("DesktopPerceptoXWorkflow.Text002"));
        }
    }

    private static bool IsSameOrNested(string root, string candidate)
    {
        string relation = Path.GetRelativePath(root, candidate);
        return relation == "." ||
            (!Path.IsPathRooted(relation) && relation != ".." &&
             !relation.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private async Task<List<SimilarImageItem>> FindSimilarAsyncCore(SimilarityQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using WorkspaceLease lease = WorkspaceLease.Acquire(query.Workspace.DatabasePath, query.Workspace.ThumbnailCacheRoot);
        if (Path.Exists(query.Workspace.DatabasePath + ".refresh-required"))
            throw new InvalidOperationException(UiText.T("DesktopPerceptoXWorkflow.Text003"));
        if (!File.Exists(query.Workspace.DatabasePath))
        {
            throw new FileNotFoundException(UiText.T("DesktopPerceptoXWorkflow.Text004"), query.Workspace.DatabasePath);
        }

        using ImageSharpImageProcessor processor = new(modernDecoder: _modernDecoder);
        SqliteIndexReader reader = new(query.Workspace.DatabasePath);
        var selected = reader.FindByPath(query.Workspace.LibraryRoot, query.SelectedImagePath);
        SearchIndexSnapshot snapshot = reader.LoadSnapshot(query.Workspace.LibraryRoot, processor.ProcessingProfileId);
        MultiRegionSearchIndexSnapshot regional = reader.LoadMultiRegionSnapshot(query.Workspace.LibraryRoot, processor.ProcessingProfileId);
        SimilaritySearchOptions options = new(query.TopN, query.MaxPerceptualDistance, query.MaxDifferenceDistance);
        MultiRegionSearchOptions regionalOptions = new(Math.Min(query.TopN * 4, 4000), query.MaxMultiRegionDistance, query.MinimumMatchedRegions);
        SimilaritySearchService search = new();
        SimilaritySearchResult result;
        if (selected is { IsActive: true })
            result = search.FindSimilarToSelectedImage(snapshot, regional, selected.Id, options, regionalOptions, cancellationToken);
        else
        {
            if (!ImageFileDiscovery.IsSupportedImage(query.SelectedImagePath) && !SupportsModern(query.SelectedImagePath))
                throw new NotSupportedException(UiText.T("DesktopPerceptoXWorkflow.Text005"));
            FileInfo file = new(query.SelectedImagePath);
            string version = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            ImageProcessingResult processed = await processor.ProcessAsync(new(file.FullName, 1, version,
                Path.Combine(query.Workspace.ThumbnailCacheRoot, "queries")), cancellationToken).ConfigureAwait(false);
            result = search.FindSimilarToExternalImage(snapshot, regional, processed.Fingerprints, file.Length,
                options, regionalOptions, cancellationToken);
        }

        IReadOnlyDictionary<long, SearchImageDetail> details = reader.LoadDetails(result.Matches.Select(match => match.ImageId));
        await EnsureThumbnailsAsync(details.Values, query.Workspace.ThumbnailCacheRoot, processor, cancellationToken).ConfigureAwait(false);
        List<SimilarImageItem> items = new(result.Matches.Count);
        foreach (SimilarityMatch match in result.Matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!details.TryGetValue(match.ImageId, out SearchImageDetail? detail) || !detail.Image.IsActive)
            {
                continue;
            }

            items.Add(new SimilarImageItem(match.ImageId, detail.Image.FilePath,
                CreateThumbnailUri(query.Workspace.ThumbnailCacheRoot, detail), match.ScorePercent,
                match.PerceptualDistance, match.DifferenceDistance, match.MultiRegionBestDistance,
                match.MultiRegionMatchedRegions));
        }

        return items;
    }


    private static async Task EnsureThumbnailsAsync(IEnumerable<SearchImageDetail> details, string cacheRoot,
        ImageSharpImageProcessor processor, CancellationToken cancellationToken)
    {
        foreach (SearchImageDetail detail in details)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (detail.Thumbnail is null || CreateThumbnailUri(cacheRoot, detail) is not null) continue;
            FileInfo original = new(detail.Image.FilePath);
            if (!original.Exists || original.Length != detail.Image.FileSize ||
                original.LastWriteTimeUtc.Ticks != detail.Image.LastWriteTimeUtc.UtcTicks) continue;
            // Regenerate only the exact version the index describes; changed originals need reindexing.
            try
            {
                await processor.RegenerateThumbnailAsync(new ImageProcessingRequest(detail.Image.FilePath,
                    detail.Image.Id, detail.Thumbnail.SourceVersion, cacheRoot), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SixLabors.ImageSharp.ImageFormatException)
            { /* A missing preview must not discard an otherwise valid matching result. */ }
        }
    }

    private static Uri? CreateThumbnailUri(string cacheRoot, SearchImageDetail detail)
    {
        if (detail.Thumbnail is null)
        {
            return null;
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
        string relative = detail.Thumbnail.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(root, relative));
        ManagedPaths.RejectLinks(candidate);
        string relation = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relation) || relation == ".." ||
            relation.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(candidate))
        {
            return null;
        }

        return new Uri(candidate);
    }

    private static void EnsureOutputDirectories(WorkspaceConfiguration workspace)
    {
        string databasePath = Path.GetFullPath(workspace.DatabasePath);
        string? databaseDirectory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectory))
        {
            throw new InvalidOperationException(UiText.T("DesktopPerceptoXWorkflow.Text006"));
        }

        Directory.CreateDirectory(databaseDirectory);
        Directory.CreateDirectory(Path.GetFullPath(workspace.ThumbnailCacheRoot));
    }

    private sealed record PendingBatchMatch(
        string QueryPath,
        SimilaritySearchResult? Result,
        string? ErrorMessage,
        Uri? ThumbnailUri = null, string? QueryVersion = null, int RegionMeasurementDistance = 1);
}
