using PerceptoX.Application.Maintenance;
using PerceptoX.Infrastructure.Exporting;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Maintenance;
using PerceptoX.Infrastructure.Persistence;
using PerceptoX.Presentation.Services;
using PerceptoX.WinUI.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.Infrastructure.Tests;

public sealed class MaintenanceIntegrationTests
{
    [Fact]
    public async Task CacheCleanupPreservesHashesAndRecreatesOnlyMatchedPreviews()
    {
        using Fixture fixture = new();
        DesktopPerceptoXWorkflow workflow = new();
        await workflow.IndexAsync(fixture.Workspace, CancellationToken.None);
        using ImageSharpImageProcessor processor = new();
        SqliteIndexReader reader = new(fixture.Workspace.DatabasePath);
        var before = reader.LoadSnapshot(fixture.Library, processor.ProcessingProfileId).Entries.ToArray();
        MaintenanceService service = new();
        MaintenancePlan plan = await service.PreviewAsync(fixture.Request(MaintenanceLevel.Cache), CancellationToken.None);
        Assert.True(plan.Files.Count > 0);
        MaintenanceResult cleanup = await service.ExecuteAsync(plan, CancellationToken.None);
        Assert.Empty(cleanup.Errors);
        Assert.False(cleanup.IndexRemoved);
        Assert.False(Directory.Exists(fixture.Workspace.ThumbnailCacheRoot));
        Assert.Equal(before, reader.LoadSnapshot(fixture.Library, processor.ProcessingProfileId).Entries.ToArray());

        IndexingSummary repeat = await workflow.IndexAsync(fixture.Workspace, CancellationToken.None);
        Assert.Equal(0, repeat.Processed);
        Assert.Equal(2, repeat.SkippedUnchanged);
        Assert.Empty(Directory.EnumerateFiles(fixture.Workspace.ThumbnailCacheRoot, "*.jpg", SearchOption.AllDirectories));
        var matches = await workflow.FindSimilarAsync(new(fixture.Workspace, fixture.Original, 10, 2, 2, 1, 1), CancellationToken.None);
        Assert.Single(matches);
        Assert.True(File.Exists(matches[0].ThumbnailUri!.LocalPath));
        Assert.Equal(before, reader.LoadSnapshot(fixture.Library, processor.ProcessingProfileId).Entries.ToArray());
    }

    [Fact]
    public async Task DeepResetClosesSqliteAndPreservesOriginalsAndCanReindex()
    {
        using Fixture fixture = new();
        DesktopPerceptoXWorkflow workflow = new();
        await workflow.IndexAsync(fixture.Workspace, CancellationToken.None);
        byte[] source = File.ReadAllBytes(fixture.Original);
        MaintenanceService service = new();
        var plan = await service.PreviewAsync(fixture.Request(MaintenanceLevel.IndexAndCache), CancellationToken.None);
        Assert.Contains(fixture.Library, plan.AffectedLibraries);
        var result = await service.ExecuteAsync(plan, CancellationToken.None);
        Assert.True(result.IndexRemoved);
        Assert.Empty(result.Errors);
        Assert.False(File.Exists(fixture.Workspace.DatabasePath));
        Assert.False(File.Exists(fixture.Workspace.DatabasePath + "-wal"));
        Assert.Equal(source, File.ReadAllBytes(fixture.Original));
        Assert.Equal(2, (await workflow.IndexAsync(fixture.Workspace, CancellationToken.None)).Processed);
    }

    [Fact]
    public async Task ChangedPreviewAndConcurrentWorkflowPreventDeletion()
    {
        using Fixture fixture = new();
        await new DesktopPerceptoXWorkflow().IndexAsync(fixture.Workspace, CancellationToken.None);
        MaintenanceService service = new();
        var plan = await service.PreviewAsync(fixture.Request(MaintenanceLevel.Cache), CancellationToken.None);
        string thumbnail = plan.Files.First(file => file.Path.EndsWith(".jpg", StringComparison.Ordinal)).Path;
        File.AppendAllText(thumbnail, "changed");
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(plan, CancellationToken.None));
        Assert.True(File.Exists(thumbnail));
        using WorkspaceLease lease = WorkspaceLease.Acquire(fixture.Workspace.DatabasePath, fixture.Workspace.ThumbnailCacheRoot);
        await Assert.ThrowsAsync<IOException>(() => service.PreviewAsync(fixture.Request(MaintenanceLevel.IndexAndCache), CancellationToken.None));
        Assert.True(File.Exists(fixture.Workspace.DatabasePath));
    }

    [Fact]
    public async Task LockedDatabasePreventsAnyCacheDeletion()
    {
        using Fixture fixture = new();
        await new DesktopPerceptoXWorkflow().IndexAsync(fixture.Workspace, CancellationToken.None);
        MaintenanceService service = new();
        var plan = await service.PreviewAsync(fixture.Request(MaintenanceLevel.IndexAndCache), CancellationToken.None);
        using FileStream externalReader = new(fixture.Workspace.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(plan, CancellationToken.None));
        Assert.All(plan.Files, file => Assert.True(File.Exists(file.Path)));
    }

    [Fact]
    public async Task ReportsCleanupLeavesUnrelatedReportsAndPreviewAssets()
    {
        using Fixture fixture = new();
        string reports = Path.Combine(fixture.Root, "reports");
        string generated = BatchReportExporter.Export(new("summary", []), reports, true);
        string unrelated = Path.Combine(reports, "my-report.csv");
        File.WriteAllText(unrelated, "keep this");
        MaintenanceService service = new();
        var plan = await service.PreviewAsync(fixture.Request(MaintenanceLevel.Reports) with { ReportRoot = reports }, CancellationToken.None);
        Assert.Single(plan.Files);
        var result = await service.ExecuteAsync(plan, CancellationToken.None);
        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(generated));
        Assert.True(File.Exists(unrelated));
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(generated)!, "assets")));
    }

    [Fact]
    public async Task ProtectedLibraryAndUnrecognizedCacheFilesArePreserved()
    {
        using Fixture fixture = new();
        await new DesktopPerceptoXWorkflow().IndexAsync(fixture.Workspace, CancellationToken.None);
        string unrelated = Path.Combine(fixture.Workspace.ThumbnailCacheRoot, "family-photo.jpg");
        File.Copy(fixture.Original, unrelated);
        MaintenanceService service = new();
        var request = fixture.Request(MaintenanceLevel.Cache);
        await Assert.ThrowsAsync<IOException>(() => service.PreviewAsync(request with { CacheRoot = fixture.Library }, CancellationToken.None));
        var plan = await service.PreviewAsync(request, CancellationToken.None);
        Assert.Equal(1, plan.PreservedFiles);
        await service.ExecuteAsync(plan, CancellationToken.None);
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(fixture.Original));
    }

    [Fact]
    public async Task CancellationBeforeExecutionPreservesConfirmedInventory()
    {
        using Fixture fixture = new();
        await new DesktopPerceptoXWorkflow().IndexAsync(fixture.Workspace, CancellationToken.None);
        MaintenanceService service = new();
        var plan = await service.PreviewAsync(fixture.Request(MaintenanceLevel.IndexAndCache), CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(plan, cancellation.Token));
        Assert.All(plan.Files, file => Assert.True(File.Exists(file.Path)));
    }

    [Theory]
    [InlineData(MaintenanceLevel.Cache)]
    [InlineData(MaintenanceLevel.IndexAndCache)]
    public async Task CancellationAfterFirstDeletionReportsPartialResultAndPreservesOriginals(MaintenanceLevel level)
    {
        using Fixture fixture = new();
        await new DesktopPerceptoXWorkflow().IndexAsync(fixture.Workspace, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        MaintenanceService service = new();
        var request = fixture.Request(level) with { Progress = new InlineProgress(_ => cancellation.Cancel()) };
        var plan = await service.PreviewAsync(request, CancellationToken.None);
        var result = await service.ExecuteAsync(plan, cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.True(result.Deleted > 0);
        Assert.True(result.Deleted < plan.Files.Count);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(fixture.Original));
        Assert.Equal(level == MaintenanceLevel.IndexAndCache, result.IndexRemoved);
        if (result.IndexRemoved)
        {
            Assert.False(File.Exists(fixture.Workspace.DatabasePath + "-wal"));
            Assert.False(File.Exists(fixture.Workspace.DatabasePath + "-shm"));
        }
        Assert.Equal(result.FreedBytes, plan.Files.Where(file => !File.Exists(file.Path)).Sum(file => file.Length));
    }

    [Fact]
    public async Task JunctionInsertedAfterFirstDeletionStopsWithoutTouchingExternalFixture()
    {
        using Fixture fixture = new();
        await new DesktopPerceptoXWorkflow().IndexAsync(fixture.Workspace, CancellationToken.None);
        string external = Path.Combine(fixture.Root, "external"), preserved = Path.Combine(external, "family-photo.jpg");
        Directory.CreateDirectory(external); File.Copy(fixture.Original, preserved);
        byte[] before = File.ReadAllBytes(preserved);
        MaintenancePlan? confirmed = null;
        string? junction = null;
        bool replaced = false;
        var request = fixture.Request(MaintenanceLevel.Cache) with { Progress = new InlineProgress(_ =>
        {
            if (replaced) return;
            string next = confirmed!.Files.First(file => file.Path.EndsWith(".jpg", StringComparison.Ordinal)).Path;
            junction = Path.GetDirectoryName(next)!;
            Assert.True(ManagedPaths.Contains(fixture.Root, junction));
            Directory.Move(junction, Path.Combine(fixture.Root, "saved-thumbnails"));
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in new[] { "/c", "mklink", "/J", junction, external }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            Assert.True(process.WaitForExit(5000)); Assert.Equal(0, process.ExitCode);
            replaced = true;
        }) };
        MaintenanceService service = new();
        confirmed = await service.PreviewAsync(request, CancellationToken.None);
        try
        {
            var result = await service.ExecuteAsync(confirmed, CancellationToken.None);
            Assert.True(replaced); Assert.NotEmpty(result.Errors);
            Assert.True(result.Deleted < confirmed.Files.Count);
            Assert.Equal(before, File.ReadAllBytes(preserved));
            Assert.True(File.Exists(fixture.Original));
        }
        finally
        {
            if (junction is not null && Directory.Exists(junction) &&
                (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0) Directory.Delete(junction, recursive: false);
        }
    }

    [Fact]
    public async Task StaleIndexBlocksDesktopAndReaderUntilCompleteScanPublishesUnderLease()
    {
        using Fixture fixture = new();
        DesktopPerceptoXWorkflow workflow = new();
        await workflow.IndexAsync(fixture.Workspace, CancellationToken.None);
        using (WorkspaceLease lease = WorkspaceLease.Acquire(fixture.Workspace.DatabasePath, fixture.Workspace.ThumbnailCacheRoot))
            IndexRefreshGuard.Mark(fixture.Workspace.DatabasePath, [fixture.Library]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.FindSimilarAsync(
            new(fixture.Workspace, fixture.Original, 10, 2, 2, 1, 1), CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => new SqliteIndexReader(fixture.Workspace.DatabasePath).CountActive(fixture.Library));
        await workflow.IndexAsync(fixture.Workspace, CancellationToken.None);
        Assert.False(Path.Exists(fixture.Workspace.DatabasePath + ".refresh-required"));
        Assert.Single(await workflow.FindSimilarAsync(new(fixture.Workspace, fixture.Original, 10, 2, 2, 1, 1), CancellationToken.None));
    }

    private sealed class InlineProgress(Action<MaintenanceProgress> report) : IProgress<MaintenanceProgress>
    {
        public void Report(MaintenanceProgress value) => report(value);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "PerceptoX-maintenance-tests", Guid.NewGuid().ToString("N"));
        public string Library => Path.Combine(Root, "library");
        public string Original => Path.Combine(Library, "original.png");
        public WorkspaceConfiguration Workspace => new(Library, Path.Combine(Root, "index.db"), Path.Combine(Root, "thumbs"));
        public Fixture()
        {
            Directory.CreateDirectory(Library);
            using Image<Rgba32> image = new(64, 40, new Rgba32(30, 80, 120));
            image.SaveAsPng(Original);
            File.Copy(Original, Path.Combine(Library, "copy.png"));
        }
        public MaintenanceRequest Request(MaintenanceLevel level) => new(Workspace.DatabasePath, Workspace.ThumbnailCacheRoot, [Library], level);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
