using Microsoft.Data.Sqlite;
using PerceptoX.Application.Matching;
using PerceptoX.Infrastructure.Exporting;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Persistence;
using PerceptoX.Presentation.Services;
using PerceptoX.WinUI.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PerceptoX.Infrastructure.Tests;

public sealed class SimilarityGroupingIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PerceptoX-group-tests", Guid.NewGuid().ToString("N"));
    private string Library => Path.Combine(_root, "library");
    private WorkspaceConfiguration Workspace => new(Library, Path.Combine(_root, "data", "index.db"), Path.Combine(_root, "data", "thumbs"));
    private readonly DesktopPerceptoXWorkflow _indexer = new();
    private DesktopGroupingWorkflow Workflow => new(_indexer);
    private Task<GroupingAnalysisResult> Analyze(params string[] roots)
        => Workflow.AnalyzeGroupsAsync(new(Workspace, roots.Length == 0 ? [Library] : roots, false, 97), CancellationToken.None);

    private string Save(string name, bool uniform = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Library, name))!);
        string path = Path.Combine(Library, name);
        using Image<Rgba32> image = new(320, 240);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                image[x, y] = uniform ? new(30, 60, 120) : new((byte)(30 + x / 2), (byte)(20 + y / 2), (byte)(20 + (x + y) / 3));
        image.SaveAsPng(path);
        return path;
    }

    [Fact]
    public async Task MultiFolderBinaryDuplicatesArePagedAndCopiedWithoutOverwrite()
    {
        string original = Save("original.png");
        string second = Path.Combine(_root, "second"); Directory.CreateDirectory(second);
        File.Copy(original, Path.Combine(second, "copy.png"));
        var result = await Analyze(Library, second);
        Assert.Equal(2, result.Images); Assert.Equal(1, result.Groups); Assert.Equal(2, result.Members);
        var groups = await Workflow.LoadGroupPageAsync(Workspace, result, 0, null, CancellationToken.None);
        var members = await Workflow.LoadGroupPageAsync(Workspace, result, 0, groups[0].GroupNumber, CancellationToken.None);
        Assert.True(members[0].IsRepresentative);
        Assert.True(members[1].BinaryIdentical);
        Assert.All(members, row => Assert.NotNull(row.ThumbnailUri));
        string destination = Path.Combine(_root, "out"); Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, members[0].FileName), "existing");
        var copied = await Workflow.CopyGroupedAsync(Workspace, result, new(false, new HashSet<long>()), destination, CancellationToken.None);
        Assert.Equal(1, copied.Copied); Assert.Equal(0, copied.Failed);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destination, members[0].FileName)));
        Assert.True(File.Exists(original));
        var inverse = await Workflow.CopyGroupedAsync(Workspace, result, new(true, new HashSet<long>()), Path.Combine(_root, "inverse"), CancellationToken.None);
        Assert.Equal(1, inverse.Copied);
    }

    [Fact]
    public async Task ResizeSelectsLargestAndRegeneratesMissingVerificationThumbnail()
    {
        string original = Save("original.png");
        using (var image = Image.Load<Rgba32>(original))
        { image.Mutate(context => context.Resize(160, 120)); image.SaveAsPng(Path.Combine(Library, "small.png")); }
        await _indexer.IndexAsync(Workspace, CancellationToken.None);
        foreach (string file in Directory.EnumerateFiles(Workspace.ThumbnailCacheRoot, "*.jpg", SearchOption.AllDirectories)) File.Delete(file);
        var result = await Analyze();
        Assert.Equal(1, result.Groups); Assert.Equal(2, result.Members);
        var members = await Workflow.LoadGroupPageAsync(Workspace, result, 0, 1, CancellationToken.None);
        Assert.Equal(320, members[0].Width);
        Assert.False(members[1].BinaryIdentical);
        Assert.All(members, item => Assert.NotNull(item.ThumbnailUri));
    }

    [Fact]
    public async Task DifferentSolidColorsDoNotBecomePerceptualDuplicates()
    {
        string original = Save("blue.png", true);
        using (Image<Rgba32> image = new(320, 240, new(200, 40, 20))) image.SaveAsPng(Path.Combine(Library, "red.png"));
        File.Copy(original, Path.Combine(Library, "blue-copy.png"));
        var result = await Analyze();
        Assert.Equal(1, result.Groups); Assert.Equal(2, result.Members);
        var members = await Workflow.LoadGroupPageAsync(Workspace, result, 0, 1, CancellationToken.None);
        Assert.DoesNotContain(members, item => item.FileName == "red.png");
    }

    [Fact]
    public async Task ReindexInvalidatesPreviouslyPublishedGroupGeneration()
    {
        string source = Save("original.png"); File.Copy(source, Path.Combine(Library, "copy.png"));
        var result = await Analyze();
        await _indexer.IndexAsync(Workspace, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Workflow.LoadGroupPageAsync(Workspace, result, 0, null, CancellationToken.None));
    }

    [Fact]
    public async Task ChangedSourceCannotBeCopiedUsingOldAnalysis()
    {
        string source = Save("original.png"); File.Copy(source, Path.Combine(Library, "copy.png"));
        var result = await Analyze();
        var members = await Workflow.LoadGroupPageAsync(Workspace, result, 0, 1, CancellationToken.None);
        File.AppendAllText(members[0].FilePath, "changed");
        var outcome = await Workflow.CopyGroupedAsync(Workspace, result, new(false, new HashSet<long>()), Path.Combine(_root, "out"), CancellationToken.None);
        Assert.Equal(0, outcome.Copied); Assert.Equal(1, outcome.Failed);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "out")));
    }

    [Fact]
    public async Task CancelledPublicationRollsBackAndKeepsPreviousCompleteRun()
    {
        string source = Save("original.png"); File.Copy(source, Path.Combine(Library, "copy.png"));
        var result = await Analyze();
        using ImageSharpImageProcessor processor = new();
        var snapshot = new SqliteIndexReader(Workspace.DatabasePath).LoadGroupingSnapshot([Library], processor.ProcessingProfileId);
        var groups = SimilarityGroupingService.Build(snapshot.Fingerprints);
        Assert.Throws<OperationCanceledException>(() => new SqliteSimilarityStore(Workspace.DatabasePath)
            .Publish(snapshot, groups, 97, 0, 0, new CancellationToken(true)));
        var page = await Workflow.LoadGroupPageAsync(Workspace, result, 0, null, CancellationToken.None);
        Assert.Single(page);
    }

    [Fact]
    public async Task ReportsIncludeAllMembersAndPortablePreviewAssets()
    {
        string source = Save("original.png"); File.Copy(source, Path.Combine(Library, "copy.png"));
        var result = await Analyze();
        var selection = new GroupingSelection(false, new HashSet<long>());
        string csv = await Workflow.ExportGroupsAsync(Workspace, result, selection, Path.Combine(_root, "reports"), false, CancellationToken.None);
        string html = await Workflow.ExportGroupsAsync(Workspace, result, selection, Path.Combine(_root, "reports"), true, CancellationToken.None);
        Assert.Contains("original.png", File.ReadAllText(csv), StringComparison.Ordinal);
        Assert.Contains("copy.png", File.ReadAllText(csv), StringComparison.Ordinal);
        Assert.Contains("assets/1.jpg", File.ReadAllText(html), StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(Path.GetDirectoryName(html)!, "assets")).Length);
    }

    [Fact]
    public async Task CopyDestinationInsideAnalyzedLibraryIsRejected()
    {
        string source = Save("original.png"); File.Copy(source, Path.Combine(Library, "copy.png"));
        var result = await Analyze();
        await Assert.ThrowsAsync<IOException>(() => Workflow.CopyGroupedAsync(Workspace, result, new(false, new HashSet<long>()),
            Path.Combine(Library, "output"), CancellationToken.None));
    }

    [Fact]
    public async Task WholeIndexIncludesPreviouslyIndexedOtherFolder()
    {
        string source = Save("original.png");
        string other = Path.Combine(_root, "other"); Directory.CreateDirectory(other); File.Copy(source, Path.Combine(other, "copy.png"));
        await _indexer.IndexAsync(Workspace with { LibraryRoot = other }, CancellationToken.None);
        var result = await Workflow.AnalyzeGroupsAsync(new(Workspace, [Library], true, 97), CancellationToken.None);
        Assert.Equal(2, result.Roots.Count); Assert.Equal(1, result.Groups);
    }

    [Fact]
    public async Task NestedScopesDoNotIndexTheSamePhysicalPathTwice()
    {
        string source = Save("nested/original.png"); File.Copy(source, Path.Combine(Library, "copy.png"));
        var result = await Analyze(Library, Path.Combine(Library, "nested"));
        Assert.Single(result.Roots); Assert.Equal(2, result.Images); Assert.Equal(1, result.Groups);
    }

    [Fact]
    public async Task RecompressionIsVerifiedAsVariantNotBinaryDuplicate()
    {
        string original = Save("original.png");
        using (var image = Image.Load<Rgba32>(original))
            image.SaveAsJpeg(Path.Combine(Library, "compressed.jpg"), new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 90 });
        var result = await Analyze();
        Assert.Equal(1, result.Groups);
        var rows = await Workflow.LoadGroupPageAsync(Workspace, result, 0, 1, CancellationToken.None);
        Assert.Equal(2, rows.Count); Assert.False(rows[1].BinaryIdentical);
    }

    [Fact]
    public async Task VersionedContentHashesAreReusedAndNotBasedOnMetadataEquality()
    {
        string original = Save("original.png"); File.Copy(original, Path.Combine(Library, "copy.png"));
        await Analyze();
        using var connection = new SqliteConnection($"Data Source={Workspace.DatabasePath};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM ImageContentHashes WHERE length(Hash)=32;";
        Assert.Equal(2L, command.ExecuteScalar());
        await Analyze();
        Assert.Equal(2L, command.ExecuteScalar());
    }

    [Fact]
    public async Task FeatureSchemaV1MigratesTransactionallyWithoutChangingIndexTables()
    {
        string original = Save("original.png"); File.Copy(original, Path.Combine(Library, "copy.png"));
        await Analyze();
        using (var connection = new SqliteConnection($"Data Source={Workspace.DatabasePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE SimilarityRuns DROP COLUMN Excluded; UPDATE SimilarityFeatureVersion SET Version=1; DROP TABLE SimilarityGroups;";
            command.ExecuteNonQuery();
        }
        var result = await Analyze(); Assert.Equal(1, result.Groups);
        using var final = new SqliteConnection($"Data Source={Workspace.DatabasePath};Pooling=False"); final.Open();
        using var query = final.CreateCommand(); query.CommandText = "SELECT Version FROM SimilarityFeatureVersion;";
        Assert.Equal(2L, query.ExecuteScalar()); query.CommandText = "SELECT COUNT(*) FROM SimilarityRuns;";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public async Task IncompatibleProfilesAreReportedRatherThanSilentlyCountedAsScanned()
    {
        Save("original.png"); await _indexer.IndexAsync(Workspace, CancellationToken.None);
        var snapshot = new SqliteIndexReader(Workspace.DatabasePath).LoadGroupingSnapshot([Library], "another-profile");
        Assert.Empty(snapshot.Fingerprints.Entries.ToArray()); Assert.Equal(1, snapshot.ExcludedImages);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
