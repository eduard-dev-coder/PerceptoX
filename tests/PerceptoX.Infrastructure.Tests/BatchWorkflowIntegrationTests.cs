using PerceptoX.Presentation.Services;
using PerceptoX.WinUI.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using PerceptoX.Application.Indexing;
using PerceptoX.Infrastructure.Persistence;

namespace PerceptoX.Infrastructure.Tests;

public sealed class BatchWorkflowIntegrationTests
{
    [Fact]
    public async Task CancellationAfterIndexCommitDoesNotErasePublishedOriginals()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-batch-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string library = Path.Combine(root, "library"), queries = Path.Combine(root, "queries");
            Directory.CreateDirectory(library); Directory.CreateDirectory(queries);
            string original = Path.Combine(library, "original.png");
            using (Image<Rgba32> image = new(16, 16)) image.SaveAsPng(original);
            File.Copy(original, Path.Combine(queries, "reference.png"));
            using CancellationTokenSource cancellation = new();
            WorkspaceConfiguration workspace = new(library, Path.Combine(root, "index.db"), Path.Combine(root, "thumbs"))
                { Progress = new InlineProgress(value => { if (value.Stage == "Completed") cancellation.Cancel(); }) };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DesktopPerceptoXWorkflow()
                .MatchFolderAsync(new(workspace, queries, 3, 2, 2, 1, 2), cancellation.Token));
            SqliteIndexReader reader = new(workspace.DatabasePath);
            Assert.Equal(1, reader.CountActive(library)); Assert.NotNull(reader.FindByPath(library, original));
            using Microsoft.Data.Sqlite.SqliteConnection connection = new($"Data Source={workspace.DatabasePath};Pooling=False"); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "SELECT Status FROM ScanRuns ORDER BY Id DESC LIMIT 1;";
            Assert.Equal("completed", command.ExecuteScalar());
            Assert.True(File.Exists(original));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class InlineProgress(Action<IndexingProgress> report) : IProgress<IndexingProgress>
    {
        public void Report(IndexingProgress value) => report(value);
    }

    [Fact]
    public async Task ExternalQueryHasBothPreviewsAllCandidatesAndPortableReportsAndCopies()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string library = Path.Combine(root, "library");
            string queries = Path.Combine(root, "queries");
            Directory.CreateDirectory(library);
            Directory.CreateDirectory(queries);
            string original = Path.Combine(library, "original.png");
            using (Image<Rgba32> image = new(120, 80))
            {
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgba32> row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                            row[x] = new((byte)(x * 13 % 256), (byte)(y * 7 % 256), (byte)((x + y) * 3 % 256));
                    }
                });
                image.SaveAsPng(original);
            }
            File.Copy(original, Path.Combine(library, "second.png"));
            File.Copy(original, Path.Combine(queries, "reference.png"));
            File.WriteAllText(Path.Combine(queries, "broken.png"), "not an image");
            System.Collections.Concurrent.ConcurrentQueue<IndexingProgress> reports = new();
            WorkspaceConfiguration workspace = new(library, Path.Combine(root, "index.db"), Path.Combine(root, "thumbs"))
                { Progress = new InlineProgress(reports.Enqueue) };
            BatchMatchSummary result = await new DesktopPerceptoXWorkflow().MatchFolderAsync(
                new(workspace, queries, 3, 2, 2, 1, 2), CancellationToken.None);
            Assert.Equal(2, result.TotalQueries);
            Assert.Equal(1, result.Failed);
            Assert.Contains(reports, value => value.Activity == "Queries" && value.Stage == "Loading");
            IndexingProgress finalProgress = reports.Last();
            Assert.Equal("Results", finalProgress.Stage); Assert.Equal(2, finalProgress.Total);
            Assert.Equal(1, finalProgress.Processed); Assert.Equal(1, finalProgress.Failed);
            BatchMatchItem match = Assert.Single(result.Items, item => item.Status == BatchMatchStatus.NearIdentical);
            Assert.True(File.Exists(match.QueryThumbnailUri!.LocalPath));
            Assert.Equal(2, match.Candidates.Count);
            Assert.All(match.Candidates, candidate => Assert.True(File.Exists(candidate.ThumbnailUri!.LocalPath)));
            DesktopBatchResultActions actions = new();
            string report = await actions.ExportAsync(result, Path.Combine(root, "reports"), true, CancellationToken.None);
            Assert.True(File.Exists(report));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(Path.GetDirectoryName(report)!, "assets")).Length);
            CopyOutcome copy = await actions.CopyAsync(match.Candidates.Select(candidate => candidate.FilePath).ToArray(),
                Path.Combine(root, "copies"), CancellationToken.None);
            Assert.Equal(2, copy.Copied);
            Assert.Equal(0, copy.Failed);
            Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(Path.Combine(root, "copies", "original.png")));
            DateTime indexWrite = File.GetLastWriteTimeUtc(workspace.DatabasePath);
            var externalMatches = await new DesktopPerceptoXWorkflow().FindSimilarAsync(
                new(workspace, Path.Combine(queries, "reference.png"), 3, 2, 2, 1, 2), CancellationToken.None);
            Assert.Equal(2, externalMatches.Count);
            Assert.Equal(indexWrite, File.GetLastWriteTimeUtc(workspace.DatabasePath));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
