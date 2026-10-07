using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using PerceptoX.Application.Imaging;
using PerceptoX.Application.Indexing;
using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;
using PerceptoX.Infrastructure.Indexing;
using PerceptoX.Infrastructure.Persistence;

namespace PerceptoX.Infrastructure.Tests;

public sealed class ImageIndexerTests
{
    [Fact]
    public async Task CancellationInsideStagingTransactionRollsBackAlreadyInsertedRows()
    {
        using TestLibrary library = new(); string first = library.AddFile("first.jpg", "first");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        SqliteIndexReader reader = new(library.Request.DatabasePath);
        long previousSize = reader.FindByPath(library.Request.LibraryRoot, first)!.FileSize;
        File.AppendAllText(first, "changed"); string second = library.AddFile("new.png", "new");
        using SqliteIndexStore store = SqliteIndexStore.Open(library.Request); store.BeginScan(library.Request);
        ScanFile[] files = new[] { first, second }.Select(path =>
        {
            FileInfo info = new(path); return new ScanFile(path, WindowsPathKey.Create(path), info.Length, info.LastWriteTimeUtc.Ticks);
        }).ToArray();
        IReadOnlyList<PreparedFile> prepared = store.PrepareBatch(files, library.Request.ProcessingProfileId);
        List<ProcessedFile> results = [];
        foreach (PreparedFile file in prepared)
        {
            var result = await new FakeProcessor().ProcessAsync(new(file.File.Path, file.ImageId, file.File.SourceVersion, library.Request.ThumbnailCacheRoot));
            results.Add(ProcessedFile.Success(file, result));
        }
        using CancellationTokenSource cancellation = new();
        Assert.ThrowsAny<OperationCanceledException>(() => store.StageBatch(new CancellingList(results, cancellation), library.Request.ProcessingProfileId, cancellation.Token));
        using SqliteConnection connection = new($"Data Source={library.Request.DatabasePath};Pooling=False"); connection.Open();
        foreach (string table in new[] { "PendingImages", "PendingFingerprints", "PendingThumbnails" })
        {
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(0L, command.ExecuteScalar());
        }
        Assert.Equal(previousSize, reader.FindByPath(library.Request.LibraryRoot, first)!.FileSize);
        Assert.Equal(1, reader.CountActive(library.Request.LibraryRoot)); store.Abort();
    }

    // Cancels deterministically after the first file's SQL inserts, not before BeginTransaction.
    private sealed class CancellingList(IReadOnlyList<ProcessedFile> files, CancellationTokenSource cancellation) : IReadOnlyList<ProcessedFile>
    {
        public int Count => files.Count;
        public ProcessedFile this[int index] => files[index];
        public IEnumerator<ProcessedFile> GetEnumerator()
        {
            yield return files[0]; cancellation.Cancel(); yield return files[1];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task ProgressHasCurrentFileKnownTotalStagingAndNoLateTimerCallbacks()
    {
        using TestLibrary library = new(); library.AddFile("first.jpg", "first"); library.AddFile("second.png", "second");
        ConcurrentQueue<IndexingProgress> reports = new();
        FakeProcessor processor = new(async (request, token) =>
        {
            await Task.Delay(350, token);
            return await new FakeProcessor().ProcessAsync(request, token);
        });
        await new ImageIndexer(processor, workerCount: 1, progress: new InlineProgress(reports.Enqueue)).IndexAsync(library.Request);
        Assert.Null(reports.First().Total);
        Assert.Contains(reports, value => value.Stage == "Processing" && value.CurrentFileName is "first.jpg" or "second.png");
        Assert.Contains(reports, value => value.Total == 2);
        IndexingProgress final = reports.Last();
        Assert.Equal("Completed", final.Stage); Assert.Equal(2, final.Persisted); Assert.Equal(2, final.Processed);
        int count = reports.Count; await Task.Delay(300); Assert.Equal(count, reports.Count);
    }

    [Fact]
    public async Task CancellationAtPublicationKeepsPreviousSnapshotAndClearsAllStaging()
    {
        using TestLibrary library = new(); string path = library.AddFile("first.jpg", "first");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        SqliteIndexReader reader = new(library.Request.DatabasePath);
        var previous = reader.FindByPath(library.Request.LibraryRoot, path)!;
        long previousScan = reader.LoadSnapshot(library.Request.LibraryRoot, library.Request.ProcessingProfileId).ScanId;
        File.AppendAllText(path, "changed"); library.AddFile("new.png", "new");
        using CancellationTokenSource cancellation = new();
        InlineProgress progress = new(value => { if (value.Stage == "Publishing") cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ImageIndexer(new FakeProcessor(), progress: progress).IndexAsync(library.Request, cancellation.Token));
        Assert.Equal(previous.FileSize, reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize);
        Assert.Equal(previousScan, reader.LoadSnapshot(library.Request.LibraryRoot, library.Request.ProcessingProfileId).ScanId);
        Assert.Equal(1, reader.CountActive(library.Request.LibraryRoot));
        using SqliteConnection connection = new($"Data Source={library.Request.DatabasePath};Pooling=False"); connection.Open();
        foreach (string table in new[] { "PendingImages", "PendingFingerprints", "PendingThumbnails", "ScanSeen" })
        {
            using SqliteCommand command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(0L, command.ExecuteScalar());
        }
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Status FROM ScanRuns ORDER BY Id DESC LIMIT 1;";
            Assert.Equal("cancelled", command.ExecuteScalar());
        }
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        Assert.Equal(2, reader.CountActive(library.Request.LibraryRoot));
    }

    private sealed class InlineProgress(Action<IndexingProgress> report) : IProgress<IndexingProgress>
    {
        public void Report(IndexingProgress value) => report(value);
    }

    [Fact]
    public async Task RepeatedScanSkipsUnchangedAndDeactivatesMissingFiles()
    {
        using TestLibrary library = new();
        string first = library.AddFile("first.jpg", "first");
        string second = library.AddFile("nested/second.png", "second");
        FakeProcessor processor = new();
        ImageIndexer indexer = new(processor);
        SqliteIndexReader reader = new(library.Request.DatabasePath);

        IndexingResult initial = await indexer.IndexAsync(library.Request);
        Assert.Equal(2, initial.Processed);
        Assert.Equal(0, initial.SkippedUnchanged);
        Assert.Equal(2, reader.CountActive(library.Request.LibraryRoot));
        long originalId = reader.FindByPath(library.Request.LibraryRoot, first)!.Id;
        Assert.Equal(3, reader.CountFingerprints(originalId));
        Assert.NotNull(reader.FindThumbnail(originalId));
        var snapshot = reader.LoadSnapshot(library.Request.LibraryRoot, library.Request.ProcessingProfileId);
        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot.Entries.ToArray(), entry =>
            entry.ImageId == originalId && entry.PerceptualHash == (ulong)originalId &&
            entry.DifferenceHash == (ulong)originalId + 1);
        MultiRegionSearchIndexSnapshot regional = reader.LoadMultiRegionSnapshot(
            library.Request.LibraryRoot, library.Request.ProcessingProfileId);
        Assert.Equal(2, regional.Count);
        Assert.True(regional.TryGetHashById(originalId, out ReadOnlySpan<byte> regionalHash));
        Assert.Equal(MultiRegionHash576.ByteLength, regionalHash.Length);

        IndexingResult repeated = await indexer.IndexAsync(library.Request);
        Assert.Equal(0, repeated.Processed);
        Assert.Equal(2, repeated.SkippedUnchanged);
        Assert.Equal(2, processor.Calls);

        File.Delete(second);
        IndexingResult afterDelete = await indexer.IndexAsync(library.Request);
        Assert.Equal(1, afterDelete.Deactivated);
        Assert.Equal(1, reader.CountActive(library.Request.LibraryRoot));
        Assert.Equal(1, reader.LoadSnapshot(library.Request.LibraryRoot,
            library.Request.ProcessingProfileId).Count);
        Assert.False(reader.FindByPath(library.Request.LibraryRoot, second)!.IsActive);
        Assert.Equal(originalId, reader.FindByPath(library.Request.LibraryRoot, first)!.Id);
    }

    [Fact]
    public async Task ChangingProcessingProfileForcesReindex()
    {
        using TestLibrary library = new();
        library.AddFile("one.jpg", "first");
        FakeProcessor processor = new();
        ImageIndexer indexer = new(processor);
        await indexer.IndexAsync(library.Request);
        IndexingRequest changed = new(library.Request.LibraryRoot, library.Request.DatabasePath,
            library.Request.ThumbnailCacheRoot, "test-pipeline-v2");

        IndexingResult result = await indexer.IndexAsync(changed);

        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.SkippedUnchanged);
        Assert.Equal(2, processor.Calls);
    }

    [Fact]
    public async Task SnapshotNeverMixesFingerprintProfilesAfterPartialFailure()
    {
        using TestLibrary library = new();
        string path = library.AddFile("one.jpg", "first");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        File.AppendAllText(path, "changed");
        IndexingRequest nextProfile = new(library.Request.LibraryRoot, library.Request.DatabasePath,
            library.Request.ThumbnailCacheRoot, "test-pipeline-v2");
        await new ImageIndexer(new FakeProcessor((_, _) => throw new InvalidDataException("bad image")))
            .IndexAsync(nextProfile);
        SqliteIndexReader reader = new(library.Request.DatabasePath);

        Assert.Equal(1, reader.LoadSnapshot(library.Request.LibraryRoot,
            library.Request.ProcessingProfileId).Count);
        Assert.Equal(0, reader.LoadSnapshot(library.Request.LibraryRoot,
            nextProfile.ProcessingProfileId).Count);

        await new ImageIndexer(new FakeProcessor()).IndexAsync(nextProfile);
        Assert.Equal(0, reader.LoadSnapshot(library.Request.LibraryRoot,
            library.Request.ProcessingProfileId).Count);
        Assert.Equal(1, reader.LoadSnapshot(library.Request.LibraryRoot,
            nextProfile.ProcessingProfileId).Count);
    }

    [Fact]
    public void RequestRejectsDatabaseAndCacheInsideLibrary()
    {
        using TestLibrary library = new();
        Assert.Throws<ArgumentException>(() => new IndexingRequest(library.Request.LibraryRoot,
            Path.Combine(library.Request.LibraryRoot, "index.db"), library.Request.ThumbnailCacheRoot, "test"));
        Assert.Throws<ArgumentException>(() => new IndexingRequest(library.Request.LibraryRoot,
            library.Request.DatabasePath, Path.Combine(library.Request.LibraryRoot, "cache"), "test"));
    }

    [Fact]
    public void WindowsPathKeyNormalizesCaseAndUnicodeComposition()
    {
        string composed = Path.Combine("C:\\", "Images", "caf\u00e9.jpg");
        string decomposed = Path.Combine("c:\\", "images", "cafe\u0301.JPG");

        Assert.Equal(WindowsPathKey.Create(composed), WindowsPathKey.Create(decomposed));
    }

    [Fact]
    public async Task FingerprintForeignKeyRejectsAnUnknownImage()
    {
        using TestLibrary library = new();
        library.AddFile("one.jpg", "first");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        using SqliteConnection connection = new($"Data Source={library.Request.DatabasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            INSERT INTO ImageFingerprints
            (ImageId,AlgorithmId,AlgorithmVersion,ProfileId,BitLength,SampleWidth,SampleHeight,HashBytes)
            VALUES(-1,'phash',1,'test',64,32,32,x'0000000000000000');
            """;

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public async Task ChangedFileIsReprocessedButKeepsItsIdentity()
    {
        using TestLibrary library = new();
        string path = library.AddFile("one.jpg", "a");
        FakeProcessor processor = new();
        ImageIndexer indexer = new(processor);
        SqliteIndexReader reader = new(library.Request.DatabasePath);
        await indexer.IndexAsync(library.Request);
        long id = reader.FindByPath(library.Request.LibraryRoot, path)!.Id;

        File.AppendAllText(path, "longer");
        IndexingResult result = await indexer.IndexAsync(library.Request);

        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.SkippedUnchanged);
        Assert.Equal(id, reader.FindByPath(library.Request.LibraryRoot, path)!.Id);
        Assert.Equal(new FileInfo(path).Length, reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize);
        Assert.Equal(2, processor.Calls);
    }

    [Fact]
    public async Task CancelledScanNeverPublishesStagedChangesOrMissingFiles()
    {
        using TestLibrary library = new();
        string first = library.AddFile("first.jpg", "first");
        string removed = library.AddFile("removed.jpg", "removed");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        SqliteIndexReader reader = new(library.Request.DatabasePath);
        File.Delete(removed);
        File.AppendAllText(first, "changed");
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProcessor blocking = new(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        });
        using CancellationTokenSource cancellation = new();
        Task<IndexingResult> scan = new ImageIndexer(blocking).IndexAsync(library.Request, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, reader.CountActive(library.Request.LibraryRoot));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.Equal(2, reader.CountActive(library.Request.LibraryRoot));
        Assert.True(reader.FindByPath(library.Request.LibraryRoot, removed)!.IsActive);
    }

    [Fact]
    public async Task PerFileFailurePreservesPreviousActiveRecord()
    {
        using TestLibrary library = new();
        string path = library.AddFile("one.jpg", "first");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        SqliteIndexReader reader = new(library.Request.DatabasePath);
        long oldSize = reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize;
        File.AppendAllText(path, "change");
        FakeProcessor failing = new((_, _) => throw new InvalidDataException("bad image"));

        IndexingResult result = await new ImageIndexer(failing).IndexAsync(library.Request);

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Processed);
        Assert.Equal(oldSize, reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize);
        Assert.Equal(1, reader.CountActive(library.Request.LibraryRoot));
    }

    [Fact]
    public async Task FailedPublishRollsBackAllActiveChanges()
    {
        using TestLibrary library = new();
        string path = library.AddFile("one.jpg", "first");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        SqliteIndexReader reader = new(library.Request.DatabasePath);
        long oldSize = reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize;
        File.AppendAllText(path, "change");
        using (SqliteConnection connection = new($"Data Source={library.Request.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER FailUpdate BEFORE UPDATE ON Images BEGIN SELECT RAISE(ABORT, 'fault injection'); END;";
            command.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<SqliteException>(() => new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request));
        Assert.Equal(oldSize, reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize);
        Assert.Equal(1, reader.CountActive(library.Request.LibraryRoot));
    }

    [Fact]
    public async Task AbandonedStageIsCleanedOnNextScan()
    {
        using TestLibrary library = new();
        string path = library.AddFile("one.jpg", "first");
        using (SqliteIndexStore store = SqliteIndexStore.Open(library.Request))
        {
            store.BeginScan(library.Request);
            FileInfo info = new(path);
            store.PrepareBatch([new ScanFile(path, WindowsPathKey.Create(path), info.Length,
                info.LastWriteTimeUtc.Ticks)], library.Request.ProcessingProfileId);
        }

        IndexingResult result = await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, new SqliteIndexReader(library.Request.DatabasePath).CountActive(library.Request.LibraryRoot));
    }

    [Fact]
    public async Task StagedUpdateIsInvisibleAndAbortKeepsPreviousSnapshot()
    {
        using TestLibrary library = new();
        string path = library.AddFile("one.jpg", "first");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        SqliteIndexReader reader = new(library.Request.DatabasePath);
        long previousSize = reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize;
        long previousScan = reader.LoadSnapshot(library.Request.LibraryRoot,
            library.Request.ProcessingProfileId).ScanId;
        File.AppendAllText(path, "changed");

        using (SqliteIndexStore store = SqliteIndexStore.Open(library.Request))
        {
            store.BeginScan(library.Request);
            FileInfo info = new(path);
            PreparedFile prepared = Assert.Single(store.PrepareBatch(
                [new ScanFile(path, WindowsPathKey.Create(path), info.Length, info.LastWriteTimeUtc.Ticks)],
                library.Request.ProcessingProfileId));
            ImageProcessingResult result = await new FakeProcessor().ProcessAsync(new ImageProcessingRequest(
                path, prepared.ImageId, prepared.File.SourceVersion, library.Request.ThumbnailCacheRoot));
            store.StageBatch([ProcessedFile.Success(prepared, result)], library.Request.ProcessingProfileId);

            Assert.Equal(previousSize, reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize);
            Assert.Equal(previousScan, reader.LoadSnapshot(library.Request.LibraryRoot,
                library.Request.ProcessingProfileId).ScanId);
            store.Abort();
        }

        Assert.Equal(previousSize, reader.FindByPath(library.Request.LibraryRoot, path)!.FileSize);
        Assert.Equal(previousScan, reader.LoadSnapshot(library.Request.LibraryRoot,
            library.Request.ProcessingProfileId).ScanId);
    }

    [Fact]
    public async Task NewerSchemaIsRejectedWithoutDeletingTheIndex()
    {
        using TestLibrary library = new();
        library.AddFile("one.jpg", "first");
        await new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request);
        using (SqliteConnection connection = new($"Data Source={library.Request.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=3;";
            command.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new ImageIndexer(new FakeProcessor()).IndexAsync(library.Request));
        Assert.Equal(1, new SqliteIndexReader(library.Request.DatabasePath).CountActive(library.Request.LibraryRoot));
    }

    private sealed class FakeProcessor : IImageProcessor
    {
        private readonly Func<ImageProcessingRequest, CancellationToken, Task<ImageProcessingResult>>? _callback;

        public FakeProcessor(Func<ImageProcessingRequest, CancellationToken, Task<ImageProcessingResult>>? callback = null) =>
            _callback = callback;

        public int Calls => Volatile.Read(ref _calls);

        public Task<ImageProcessingResult> ProcessAsync(ImageProcessingRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return _callback is null ? Task.FromResult(CreateResult(request)) : _callback(request, cancellationToken);
        }

        private int _calls;

        private static ImageProcessingResult CreateResult(ImageProcessingRequest request)
        {
            string relativePath = $"fake/{request.ImageId}-{request.SourceVersion.Replace(':', '-')}.jpg";
            string fullPath = Path.Combine(request.CacheRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, [1, 2, 3]);
            Fingerprint perceptual = new(new FingerprintDescriptor("phash", 1, "test", 64, 32, 32),
                FingerprintValue.FromUInt64((ulong)request.ImageId));
            Fingerprint difference = new(new FingerprintDescriptor("dhash", 1, "test", 64, 9, 8),
                FingerprintValue.FromUInt64((ulong)request.ImageId + 1));
            Fingerprint regional = new(new MultiRegionHash576().Descriptor,
                FingerprintValue.FromBytes(new byte[MultiRegionHash576.ByteLength],
                    MultiRegionHash576.RegionCount * MultiRegionHash576.RegionBitLength));
            ImageFingerprintSet set = new(10, 10, [perceptual, difference, regional]);
            return new ImageProcessingResult(set,
                new(request.ImageId, request.SourceVersion, "fake-v1", relativePath, "jpeg", 10, 10));
        }
    }

    private sealed class TestLibrary : IDisposable
    {
        private readonly string _directory;

        public TestLibrary()
        {
            _directory = Path.Combine(AppContext.BaseDirectory, ".test-artifacts", Guid.NewGuid().ToString("N"));
            string root = Path.Combine(_directory, "images");
            Directory.CreateDirectory(root);
            Request = new IndexingRequest(root, Path.Combine(_directory, "index.db"),
                Path.Combine(_directory, "thumbs"), "test-pipeline-v1");
        }

        public IndexingRequest Request { get; }

        public string AddFile(string relativePath, string contents)
        {
            string fullPath = Path.Combine(Request.LibraryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
