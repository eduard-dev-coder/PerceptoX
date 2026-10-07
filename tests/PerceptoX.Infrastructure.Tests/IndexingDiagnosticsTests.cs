using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PerceptoX.Application.Indexing;
using PerceptoX.Infrastructure.Diagnostics;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Indexing;
using PerceptoX.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.Data.Sqlite;

namespace PerceptoX.Infrastructure.Tests;

public sealed class IndexingDiagnosticsTests
{
    [Fact]
    public async Task IncompleteDiscoveryKeepsUnseenEntriesAndPublishesAvailableChanges()
    {
        string root = CreateRoot();
        try
        {
            string library = Path.Combine(root, "library");
            Directory.CreateDirectory(library);
            string first = Path.Combine(library, "first.png");
            string unseen = Path.Combine(library, "hidden.png");
            using (Image<Rgba32> image = new(1, 1)) { image.SaveAsPng(first); image.SaveAsPng(unseen); }
            using ImageSharpImageProcessor processor = new();
            IndexingRequest request = new(library, Path.Combine(root, "index.db"), Path.Combine(root, "thumbs"), processor.ProcessingProfileId);
            await new ImageIndexer(processor).IndexAsync(request);
            // Emulate an installed v1 database. Migration must preserve both active entries.
            using (SqliteConnection previous = new($"Data Source={request.DatabasePath};Pooling=False"))
            {
                previous.Open();
                using SqliteCommand downgrade = previous.CreateCommand();
                downgrade.CommandText = "ALTER TABLE ScanRuns DROP COLUMN DiscoveryComplete; UPDATE SchemaInfo SET Version=1; PRAGMA user_version=1;";
                downgrade.ExecuteNonQuery();
            }
            ConcurrentQueue<IndexingProgress> updates = new();
            ImageIndexer incomplete = new(processor, progress: new InlineProgress(updates), discovery: (_, issue) =>
            {
                issue(new ImageDiscoveryIssue(unseen, "UnauthorizedAccessException", false));
                return [first];
            });
            IndexingResult result = await incomplete.IndexAsync(request);
            Assert.False(result.DiscoveryComplete);
            Assert.Equal(1, result.Inaccessible);
            Assert.Equal(0, result.Deactivated);
            Assert.Equal(2, new SqliteIndexReader(request.DatabasePath).CountActive(library));
            Assert.Contains(updates, progress => progress.Stage == "Partial" && progress.Inaccessible == 1);
            using SqliteConnection upgraded = new($"Data Source={request.DatabasePath};Pooling=False");
            upgraded.Open();
            using SqliteCommand state = upgraded.CreateCommand();
            state.CommandText = "SELECT DiscoveryComplete FROM ScanRuns ORDER BY Id DESC LIMIT 1;";
            Assert.Equal(0L, state.ExecuteScalar());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UnsupportedFormatsAndCorruptFilesProduceCountsAndStructuredRelativePathLogs()
    {
        string root = CreateRoot();
        try
        {
            string library = Path.Combine(root, "library");
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(library, "broken.png"), "corrupt");
            File.WriteAllText(Path.Combine(library, "phone.heic"), "unsupported");
            File.WriteAllText(Path.Combine(library, "modern.avif"), "unsupported");
            using (Image<Rgba32> image = new(1, 1, new Rgba32(40, 20, 10, 0)))
            {
                image.SaveAsPng(Path.Combine(library, "tiny.png"));
                image.SaveAsWebp(Path.Combine(library, "tiny.webp"));
            }
            using ImageSharpImageProcessor processor = new();
            string logs = Path.Combine(root, "logs");
            IndexingResult result;
            using (ILoggerFactory logging = DiagnosticsLog.CreateFactory(logs))
            {
                result = await new ImageIndexer(processor, logger: logging.CreateLogger<ImageIndexer>())
                    .IndexAsync(new(library, Path.Combine(root, "index.db"), Path.Combine(root, "thumbs"), processor.ProcessingProfileId));
            }
            Assert.Equal(2, result.Processed);
            Assert.Equal(1, result.Failed);
            Assert.Equal(1, result.Corrupt);
            Assert.Equal(2, result.Unsupported);
            Assert.True(result.DiscoveryComplete);
            string[] lines = File.ReadAllLines(Assert.Single(Directory.GetFiles(logs, "*.jsonl")));
            Assert.Contains(lines, line => line.Contains("broken.png", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("UnsupportedFormat", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains(library, StringComparison.Ordinal));
            foreach (string line in lines) { using JsonDocument document = JsonDocument.Parse(line); Assert.True(document.RootElement.TryGetProperty("Timestamp", out _)); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-diagnostics-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    private sealed class InlineProgress(ConcurrentQueue<IndexingProgress> updates) : IProgress<IndexingProgress>
    { public void Report(IndexingProgress value) => updates.Enqueue(value); }
}
