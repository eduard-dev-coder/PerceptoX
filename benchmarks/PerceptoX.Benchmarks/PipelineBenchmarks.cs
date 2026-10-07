using System.Globalization;
using System.Security.Cryptography;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Microsoft.Data.Sqlite;
using PerceptoX.Application.Imaging;
using PerceptoX.Core.Fingerprints;
using PerceptoX.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.Benchmarks;

/// <summary>Real decoding, hashing, thumbnail writes, and SQLite; no user collection is read.</summary>
[Config(typeof(PipelineBenchmarkConfig))]
[MemoryDiagnoser]
public class PipelineBenchmarks
{
    private const int Capacity = 128;
    private const int BatchSize = 64;
    private const int ImageCount = 192;
    private string _root = string.Empty;
    private string _sourceRoot = string.Empty;
    private readonly Dictionary<string, string> _digests = new(StringComparer.Ordinal);
    private int _run;

    [Params(2, 4)]
    public int Workers { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(AppContext.BaseDirectory, ".pipeline-benchmark-artifacts", Guid.NewGuid().ToString("N"));
        _sourceRoot = Path.Combine(_root, "sources");
        Directory.CreateDirectory(_sourceRoot);
        for (int index = 0; index < ImageCount; index++)
        {
            int side = (index % 3) switch { 0 => 256, 1 => 1024, _ => 2048 };
            using Image<Rgba32> image = new(side, side);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)((x * 3 + y + index * 17) & 255),
                            (byte)((x + y * 5 + index * 11) & 255), (byte)((x * 7 + y * 2 + index * 23) & 255));
                    }
                }
            });

            string path = Path.Combine(_sourceRoot, index.ToString("D4", CultureInfo.InvariantCulture));
            if (index % 2 == 0) image.SaveAsPng(path + ".png");
            else image.SaveAsJpeg(path + ".jpg");
        }

        Console.WriteLine($"Pipeline corpus: {ImageCount} images, 96 PNG/96 JPEG; sides 256/1024/2048; " +
            $"capacity={Capacity}, batch={BatchSize}, workers={Workers}; source bytes=" +
            Directory.EnumerateFiles(_sourceRoot).Sum(path => new FileInfo(path).Length));
        // Both variants see the same files, metadata, and processor before measurement.
        // This also validates equivalence across methods (BDN creates a fixture per benchmark case).
        Channels().GetAwaiter().GetResult();
        Dataflow().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public Task<string> Channels() => RunAsync(useDataflow: false);

    [Benchmark]
    public Task<string> Dataflow() => RunAsync(useDataflow: true);

    private async Task<string> RunAsync(bool useDataflow)
    {
        string method = useDataflow ? "Dataflow" : "Channels";
        string output = Path.Combine(_root, method + "-" + Interlocked.Increment(ref _run).ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(output);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        using ImageSharpImageProcessor processor = new(maxConcurrency: Workers);
        using PipelineSink sink = new(Path.Combine(output, "index.sqlite"), BatchSize);
        string thumbnails = Path.Combine(output, "thumbs");
        if (useDataflow) await RunDataflowAsync(processor, sink, thumbnails, timeout).ConfigureAwait(false);
        else await RunChannelsAsync(processor, sink, thumbnails, timeout).ConfigureAwait(false);
        string digest = sink.FinishAndValidate(ImageCount, thumbnails);
        if (_digests.Values.Any(prior => prior != digest))
            throw new InvalidDataException("Pipeline variants produced different persisted fingerprints.");
        _digests[method] = digest;
        return digest;
    }

    private IEnumerable<ImageProcessingRequest> Discover(string thumbnails)
    {
        // Streaming filesystem discovery. IDs come from deterministic fixture names, not completion order.
        foreach (string path in Directory.EnumerateFiles(_sourceRoot))
        {
            long id = long.Parse(Path.GetFileNameWithoutExtension(path), CultureInfo.InvariantCulture) + 1;
            FileInfo metadata = new(path);
            string version = metadata.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                metadata.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            yield return new ImageProcessingRequest(path, id, version, thumbnails);
        }
    }

    private async Task RunChannelsAsync(ImageSharpImageProcessor processor, PipelineSink sink,
        string thumbnails, CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;
        Channel<ImageProcessingRequest> input = Channel.CreateBounded<ImageProcessingRequest>(
            new BoundedChannelOptions(Capacity) { SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        Channel<ImageProcessingResult> output = Channel.CreateBounded<ImageProcessingResult>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

        async Task Guard(Func<Task> action)
        {
            try { await action().ConfigureAwait(false); }
            catch { await cancellation.CancelAsync().ConfigureAwait(false); throw; }
        }

        Task discovery = Task.Run(() => Guard(async () =>
        {
            try
            {
                foreach (ImageProcessingRequest request in Discover(thumbnails))
                    await input.Writer.WriteAsync(request, token).ConfigureAwait(false);
            }
            finally { input.Writer.TryComplete(); }
        }), token);
        Task[] workers = Enumerable.Range(0, Workers).Select(_ => Task.Run(() => Guard(async () =>
        {
            await foreach (ImageProcessingRequest request in input.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                ImageProcessingResult result = await processor.ProcessAsync(request, token).ConfigureAwait(false);
                await output.Writer.WriteAsync(result, token).ConfigureAwait(false);
            }
        }), token)).ToArray();
        Task completion = Guard(async () =>
        {
            try { await Task.WhenAll(workers).ConfigureAwait(false); }
            finally { output.Writer.TryComplete(); }
        });
        Task persistence = Task.Run(() => Guard(async () =>
        {
            await foreach (ImageProcessingResult result in output.Reader.ReadAllAsync(token).ConfigureAwait(false))
                sink.Accept(result);
        }), token);
        await Task.WhenAll(discovery, completion, persistence).ConfigureAwait(false);
    }

    private async Task RunDataflowAsync(ImageSharpImageProcessor processor, PipelineSink sink,
        string thumbnails, CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;
        BufferBlock<ImageProcessingRequest> discovery = new(new DataflowBlockOptions
        { BoundedCapacity = Capacity, CancellationToken = token });
        TransformBlock<ImageProcessingRequest, ImageProcessingResult> processing = new(
            request => processor.ProcessAsync(request, token), new ExecutionDataflowBlockOptions
            {
                // Avoid adding another 128 queued inputs beyond the discovery buffer.
                BoundedCapacity = Workers, MaxDegreeOfParallelism = Workers,
                EnsureOrdered = false, CancellationToken = token
            });
        ActionBlock<ImageProcessingResult> persistence = new(sink.Accept, new ExecutionDataflowBlockOptions
        { BoundedCapacity = Capacity, MaxDegreeOfParallelism = 1, CancellationToken = token });
        DataflowLinkOptions links = new() { PropagateCompletion = true };
        using IDisposable first = discovery.LinkTo(processing, links);
        using IDisposable second = processing.LinkTo(persistence, links);

        async Task CancelOnFailure(Task task)
        {
            try { await task.ConfigureAwait(false); }
            catch { await cancellation.CancelAsync().ConfigureAwait(false); throw; }
        }

        Task producer = Task.Run(async () =>
        {
            try
            {
                foreach (ImageProcessingRequest request in Discover(thumbnails))
                {
                    if (!await discovery.SendAsync(request, token).ConfigureAwait(false))
                        throw new InvalidOperationException("Discovery block declined an image.");
                }
                discovery.Complete();
            }
            catch (Exception error)
            {
                ((IDataflowBlock)discovery).Fault(error);
                await cancellation.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }, token);
        await Task.WhenAll(producer, CancelOnFailure(discovery.Completion),
            CancelOnFailure(processing.Completion), CancelOnFailure(persistence.Completion)).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine("Pipeline persisted-content validation: " + string.Join(", ", _digests.Select(pair => pair.Key + "=" + pair.Value)));
        string parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".pipeline-benchmark-artifacts"));
        string target = Path.GetFullPath(_root);
        if (!target.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pipeline fixture cleanup escaped its dedicated parent.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private sealed class PipelineSink : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly int _batchSize;
        private readonly List<ImageProcessingResult> _batch;

        public PipelineSink(string database, int batchSize)
        {
            _batchSize = batchSize;
            _batch = new List<ImageProcessingResult>(batchSize);
            _connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = database, Pooling = false }.ToString());
            _connection.Open();
            using SqliteCommand schema = _connection.CreateCommand();
            schema.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA foreign_keys=ON;
                CREATE TABLE Images(Id INTEGER PRIMARY KEY, Width INTEGER NOT NULL, Height INTEGER NOT NULL,
                    SourceVersion TEXT NOT NULL, ThumbnailPath TEXT NOT NULL);
                CREATE TABLE Fingerprints(ImageId INTEGER NOT NULL REFERENCES Images(Id), AlgorithmId TEXT NOT NULL,
                    AlgorithmVersion INTEGER NOT NULL, ProfileId TEXT NOT NULL, BitLength INTEGER NOT NULL,
                    HashBytes BLOB NOT NULL, PRIMARY KEY(ImageId,AlgorithmId)) WITHOUT ROWID;
                """;
            schema.ExecuteNonQuery();
        }

        public void Accept(ImageProcessingResult result)
        {
            _batch.Add(result);
            if (_batch.Count == _batchSize) Flush();
        }

        private void Flush()
        {
            if (_batch.Count == 0) return;
            using SqliteTransaction transaction = _connection.BeginTransaction();
            foreach (ImageProcessingResult result in _batch)
            {
                using SqliteCommand image = _connection.CreateCommand();
                image.Transaction = transaction;
                image.CommandText = "INSERT INTO Images VALUES($id,$width,$height,$version,$thumbnail);";
                image.Parameters.AddWithValue("$id", result.Thumbnail.ImageId);
                image.Parameters.AddWithValue("$width", result.Fingerprints.Width);
                image.Parameters.AddWithValue("$height", result.Fingerprints.Height);
                image.Parameters.AddWithValue("$version", result.Thumbnail.SourceVersion);
                image.Parameters.AddWithValue("$thumbnail", result.Thumbnail.RelativePath);
                image.ExecuteNonQuery();
                foreach (Fingerprint fingerprint in result.Fingerprints.Fingerprints.Values)
                {
                    using SqliteCommand hash = _connection.CreateCommand();
                    hash.Transaction = transaction;
                    hash.CommandText = "INSERT INTO Fingerprints VALUES($image,$algorithm,$version,$profile,$bits,$bytes);";
                    hash.Parameters.AddWithValue("$image", result.Thumbnail.ImageId);
                    hash.Parameters.AddWithValue("$algorithm", fingerprint.Descriptor.AlgorithmId);
                    hash.Parameters.AddWithValue("$version", fingerprint.Descriptor.AlgorithmVersion);
                    hash.Parameters.AddWithValue("$profile", fingerprint.Descriptor.ProfileId);
                    hash.Parameters.AddWithValue("$bits", fingerprint.Descriptor.BitLength);
                    hash.Parameters.AddWithValue("$bytes", fingerprint.Value.Bytes.ToArray());
                    hash.ExecuteNonQuery();
                }
            }
            transaction.Commit();
            _batch.Clear();
        }

        public string FinishAndValidate(int expected, string thumbnails)
        {
            Flush();
            using SqliteCommand counts = _connection.CreateCommand();
            counts.CommandText = "SELECT (SELECT COUNT(*) FROM Images), (SELECT COUNT(*) FROM Fingerprints);";
            using (SqliteDataReader reader = counts.ExecuteReader())
            {
                if (!reader.Read() || reader.GetInt64(0) != expected || reader.GetInt64(1) != expected * 3L)
                    throw new InvalidDataException("Missing or duplicate persisted images/fingerprints.");
            }

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT ImageId,AlgorithmId,HashBytes FROM Fingerprints ORDER BY ImageId,AlgorithmId;";
            using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    digest.AppendData(System.Text.Encoding.UTF8.GetBytes(reader.GetInt64(0).ToString(CultureInfo.InvariantCulture) + ":" + reader.GetString(1) + ":"));
                    digest.AppendData((byte[])reader[2]);
                }
            }

            command.CommandText = "SELECT ThumbnailPath FROM Images;";
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (!File.Exists(Path.Combine(thumbnails, reader.GetString(0))))
                        throw new InvalidDataException("Missing generated thumbnail.");
                }
            }
            return Convert.ToHexString(digest.GetHashAndReset());
        }

        public void Dispose() => _connection.Dispose();
    }
}

public sealed class PipelineBenchmarkConfig : ManualConfig
{
    public PipelineBenchmarkConfig()
    {
        // In-process avoids generated-project restore. Dry strategy keeps the profile short and explicit.
        AddJob(Job.Dry.WithToolchain(InProcessNoEmitToolchain.Instance).WithWarmupCount(1)
            .WithIterationCount(3).WithInvocationCount(1).WithUnrollFactor(1).WithId("PipelineShort"));
    }
}
