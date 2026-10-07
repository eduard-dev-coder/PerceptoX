using System.Threading.Channels;
using PerceptoX.Application.Imaging;
using PerceptoX.Application.Indexing;
using PerceptoX.Infrastructure.Persistence;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PerceptoX.Infrastructure.Diagnostics;

namespace PerceptoX.Infrastructure.Indexing;

public sealed class ImageIndexer : IImageIndexer
{
    private const int ChannelCapacity = 128;
    private const int DatabaseBatchSize = 64;
    private readonly IImageProcessor _processor;
    private readonly int _workerCount;
    private readonly ILogger<ImageIndexer> _logger;
    private readonly IProgress<IndexingProgress>? _progress;
    private readonly Func<string, Action<ImageDiscoveryIssue>, IEnumerable<string>> _discovery;

    public ImageIndexer(IImageProcessor processor, int workerCount = 2, ILogger<ImageIndexer>? logger = null,
        IProgress<IndexingProgress>? progress = null,
        Func<string, Action<ImageDiscoveryIssue>, IEnumerable<string>>? discovery = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        _processor = processor;
        _workerCount = workerCount;
        _logger = logger ?? NullLogger<ImageIndexer>.Instance;
        _progress = progress;
        _discovery = discovery ?? ((root, issue) => ImageFileDiscovery.EnumerateSupportedImages(root, issue));
    }

    public async Task<IndexingResult> IndexAsync(IndexingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteIndexStore store = SqliteIndexStore.Open(request);
        store.BeginScan(request);
        long scanId = store.ScanId;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = linked.Token;
        int discovered = 0;
        int skipped = 0;
        int processed = 0;
        int failed = 0;
        int inaccessible = 0;
        int unsupported = 0;
        int corrupt = 0;
        int discoveryFinished = 0, persisted = 0, persistenceActive = 0;
        string?[] workerFileNames = new string?[_workerCount];
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (_logger.IsEnabled(LogLevel.Information)) IndexingLog.Started(_logger, scanId, Path.GetFileName(request.LibraryRoot), _workerCount);
        void Report(string stage)
        {
            if (_progress is null) return;
            try
            {
                string? currentFileName = null;
                for (int worker = 0; worker < workerFileNames.Length; worker++)
                {
                    currentFileName = Volatile.Read(ref workerFileNames[worker]);
                    if (currentFileName is not null) break;
                }
                if (stage == "Processing" && currentFileName is null && Volatile.Read(ref discoveryFinished) == 0) stage = "Discovery";
                _progress.Report(new IndexingProgress(scanId, Volatile.Read(ref discovered), Volatile.Read(ref skipped),
                    Volatile.Read(ref processed), Volatile.Read(ref failed), Volatile.Read(ref unsupported),
                    Volatile.Read(ref inaccessible), Volatile.Read(ref processed) / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds), stage)
                    { Corrupt = Volatile.Read(ref corrupt), CurrentFileName = currentFileName,
                        Total = Volatile.Read(ref discoveryFinished) != 0 ? Volatile.Read(ref discovered) : null,
                        Persisted = Volatile.Read(ref persisted) });
            }
            catch (Exception exception)
            { IndexingLog.ObserverFailed(_logger, exception.GetType().Name); }
        }
        using Timer? progressTimer = _progress is null ? null : new Timer(_ =>
            Report(Volatile.Read(ref persistenceActive) != 0 ? "Persistence" : "Processing"), null, 250, 250);
        Report("Discovery");
        void DiscoveryIssue(ImageDiscoveryIssue issue)
        {
            if (issue.Unsupported) Interlocked.Increment(ref unsupported);
            else Interlocked.Increment(ref inaccessible);
            if (_logger.IsEnabled(LogLevel.Warning)) IndexingLog.Skipped(_logger, scanId,
                Path.GetRelativePath(request.LibraryRoot, issue.Path), issue.ErrorCode);
        }

        Channel<PreparedFile> candidates = Channel.CreateBounded<PreparedFile>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        Channel<ProcessedFile> results = Channel.CreateBounded<ProcessedFile>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        Task producer = Task.Run(async () =>
        {
            try
            {
                List<ScanFile> batch = new(DatabaseBatchSize);
                foreach (string path in _discovery(request.LibraryRoot, DiscoveryIssue))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        FileInfo info = new(path);
                        batch.Add(new ScanFile(info.FullName, WindowsPathKey.Create(info.FullName),
                            info.Length, info.LastWriteTimeUtc.Ticks));
                        discovered++;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    { DiscoveryIssue(new ImageDiscoveryIssue(path, exception.GetType().Name, false)); continue; }
                    if (batch.Count == DatabaseBatchSize)
                    {
                        await PrepareAndQueueAsync(batch).ConfigureAwait(false);
                        batch.Clear();
                    }
                }

                if (batch.Count != 0)
                {
                    await PrepareAndQueueAsync(batch).ConfigureAwait(false);
                }
                Volatile.Write(ref discoveryFinished, 1);
                Report("Processing");
            }
            catch
            {
                linked.Cancel();
                throw;
            }
            finally
            {
                candidates.Writer.TryComplete();
            }
        }, CancellationToken.None);

        async Task PrepareAndQueueAsync(IReadOnlyList<ScanFile> files)
        {
            IReadOnlyList<PreparedFile> prepared;
            Interlocked.Increment(ref persistenceActive);
            try { prepared = store.PrepareBatch(files, request.ProcessingProfileId, token); }
            finally { Interlocked.Decrement(ref persistenceActive); }
            foreach (PreparedFile file in prepared)
            {
                if (file.IsUnchanged)
                {
                    skipped++;
                }
                else
                {
                    await candidates.Writer.WriteAsync(file, token).ConfigureAwait(false);
                }
            }
        }

        Task[] workers = Enumerable.Range(0, _workerCount).Select(worker => Task.Run(async () =>
        {
            try
            {
                await foreach (PreparedFile prepared in candidates.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    // With parallel workers this is one active file, not a serial queue position.
                    Volatile.Write(ref workerFileNames[worker], Path.GetFileName(prepared.File.Path));
                    ProcessedFile outcome;
                    try
                    {
                        ImageProcessingRequest processingRequest = new(prepared.File.Path, prepared.ImageId,
                            prepared.File.SourceVersion, request.ThumbnailCacheRoot);
                        ImageProcessingResult result = await _processor.ProcessAsync(processingRequest, token)
                            .ConfigureAwait(false);
                        FileInfo after = new(prepared.File.Path);
                        outcome = after.Length == prepared.File.Size &&
                            after.LastWriteTimeUtc.Ticks == prepared.File.LastWriteUtcTicks
                            ? ProcessedFile.Success(prepared, result)
                            : ProcessedFile.Failure(prepared, "SourceChanged");
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
                    {
                        outcome = ProcessedFile.Failure(prepared, exception.GetType().Name);
                        if (exception is SixLabors.ImageSharp.ImageFormatException) Interlocked.Increment(ref corrupt);
                        if (_logger.IsEnabled(LogLevel.Warning)) IndexingLog.Failed(_logger, scanId,
                            Path.GetRelativePath(request.LibraryRoot, prepared.File.Path), exception.GetType().Name);
                    }
                    finally { Volatile.Write(ref workerFileNames[worker], null); }

                    await results.Writer.WriteAsync(outcome, token).ConfigureAwait(false);
                }
            }
            catch
            {
                linked.Cancel();
                throw;
            }
        }, CancellationToken.None)).ToArray();

        Task completeResults = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            finally
            {
                results.Writer.TryComplete();
            }
        }, CancellationToken.None);

        Task writer = Task.Run(async () =>
        {
            try
            {
                List<ProcessedFile> batch = new(DatabaseBatchSize);
                await foreach (ProcessedFile result in results.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    batch.Add(result);
                    if (result.Result is null)
                    {
                        failed++;
                    }
                    else
                    {
                        processed++;
                    }

                    if (batch.Count == DatabaseBatchSize)
                    {
                        Stage(batch);
                        batch.Clear();
                    }
                }

                if (batch.Count != 0)
                {
                    Stage(batch);
                }
            }
            catch
            {
                linked.Cancel();
                throw;
            }
        }, CancellationToken.None);

        void Stage(IReadOnlyList<ProcessedFile> batch)
        {
            Interlocked.Increment(ref persistenceActive);
            try
            {
                store.StageBatch(batch, request.ProcessingProfileId, token);
                Interlocked.Add(ref persisted, batch.Count(file => file.Result is not null));
            }
            finally { Interlocked.Decrement(ref persistenceActive); }
        }

        try
        {
            await Task.WhenAll(producer, completeResults, writer).ConfigureAwait(false);
            if (progressTimer is not null) await progressTimer.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            bool complete = inaccessible == 0;
            Report("Publishing");
            int deactivated = store.Publish(complete, cancellationToken);
            Report(complete ? "Completed" : "Partial");
            if (_logger.IsEnabled(LogLevel.Information)) IndexingLog.Completed(_logger, scanId,
                stopwatch.Elapsed.TotalMilliseconds, processed, failed, unsupported, inaccessible,
                processed / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds));
            return new IndexingResult(scanId, discovered, skipped, processed, failed, deactivated)
            { Unsupported = unsupported, Corrupt = corrupt, Inaccessible = inaccessible, DiscoveryComplete = complete };
        }
        catch
        {
            linked.Cancel();
            try
            {
                await Task.WhenAll(producer, completeResults, writer).ConfigureAwait(false);
            }
            catch
            {
                // The original failure or cancellation remains the result.
            }

            if (progressTimer is not null) await progressTimer.DisposeAsync().ConfigureAwait(false);
            store.Abort();
            if (_logger.IsEnabled(LogLevel.Warning)) IndexingLog.Aborted(_logger, scanId, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

}
