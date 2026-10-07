using System.Text.Json.Nodes;
using PerceptoX.Application.Cleanup;
using PerceptoX.Infrastructure.Cleanup;
using PerceptoX.Infrastructure.Maintenance;

namespace PerceptoX.Infrastructure.Tests;

public sealed class SmartCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PerceptoX-cleanup-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HighestResolutionPreservesKeeperAndRecoverySurvivesNewService()
    {
        VerifiedCleanupGroup group = Group();
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([group], KeeperRule.HighestResolution);
        Assert.Equal(group.Files[0].Path, Assert.Single(plan.Keepers).Path);
        Assert.Equal(2, plan.Moves.Count);
        CleanupResult result = await service.ExecuteAsync(plan);
        Assert.True(File.Exists(Path.Combine(_root, "data", "library.db.refresh-required")));
        Assert.True(result.IndexRequiresRefresh);
        Assert.All(result.Entries, entry => Assert.Equal(CleanupEntryState.Archived, entry.State));
        Assert.True(File.Exists(plan.Keepers[0].Path));
        Assert.All(plan.Moves, move => { Assert.False(File.Exists(move.OriginalPath)); Assert.True(File.Exists(move.ArchivePath)); });
        RecoverableSmartCleanupService restarted = Service();
        Assert.Contains(plan.OperationId, restarted.ListRecoveryOperations());
        Assert.All((await restarted.ReadRecoveryAsync(plan.OperationId)).Entries, entry => Assert.Equal(CleanupEntryState.Archived, entry.State));
        CleanupResult undo = await restarted.UndoAsync(plan.OperationId);
        Assert.True(File.Exists(Path.Combine(_root, "data", "library.db.refresh-required")));
        Assert.All(undo.Entries, entry => Assert.Equal(CleanupEntryState.Restored, entry.State));
        Assert.All(group.Files, file => Assert.True(File.Exists(file.Path)));
    }

    [Fact]
    public async Task NewestUsesExplicitMetadataAndRefusesUnverifiedGroups()
    {
        VerifiedCleanupGroup group = Group();
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([group], KeeperRule.Newest);
        Assert.Equal(group.Files[2].Path, plan.Keepers[0].Path);
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync([group with { ManuallyVerified = false }], KeeperRule.Newest));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedSourceOrKeeperRefusesEntireOperationEvenIfTimestampAndSizeAreUnchanged(bool keeper)
    {
        VerifiedCleanupGroup group = Group();
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([group], KeeperRule.HighestResolution);
        string changed = keeper ? plan.Keepers[0].Path : plan.Moves[0].OriginalPath;
        DateTime timestamp = File.GetLastWriteTimeUtc(changed);
        string contents = File.ReadAllText(changed);
        File.WriteAllText(changed, new string('z', contents.Length));
        File.SetLastWriteTimeUtc(changed, timestamp);
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(plan));
        Assert.All(group.Files, file => Assert.True(File.Exists(file.Path)));
        Assert.False(File.Exists(Path.Combine(_root, "archive", plan.OperationId.ToString("N"), "cleanup.json")));
    }

    [Fact]
    public async Task ExistingArchiveTargetRefusesBeforeMovingAnything()
    {
        VerifiedCleanupGroup group = Group();
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([group], KeeperRule.HighestResolution);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.Moves[0].ArchivePath)!);
        File.WriteAllText(plan.Moves[0].ArchivePath, "existing");
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(plan));
        Assert.Equal("existing", File.ReadAllText(plan.Moves[0].ArchivePath));
        Assert.All(group.Files, file => Assert.True(File.Exists(file.Path)));
    }

    [Fact]
    public async Task HeldVerifiedHandlesDenyExternalRenameReplacementAndWritesBetweenMoves()
    {
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        string expected = File.ReadAllText(plan.Moves[1].OriginalPath);
        int callbacks = 0;
        CleanupResult result = await service.ExecuteAsync(plan, new ImmediateProgress(_ =>
        {
            if (++callbacks != 1) return;
            string pending = plan.Moves[1].OriginalPath;
            Assert.Throws<IOException>(() => File.Move(pending, pending + ".external"));
            Assert.Throws<IOException>(() => File.Delete(pending));
            Assert.Throws<IOException>(() => File.WriteAllText(pending, "replacement"));
            Assert.Throws<IOException>(() => File.Move(plan.Keepers[0].Path, plan.Keepers[0].Path + ".external"));
        }));
        Assert.Equal(2, callbacks);
        Assert.All(result.Entries, entry => Assert.Equal(CleanupEntryState.Archived, entry.State));
        Assert.Equal(expected, File.ReadAllText(plan.Moves[1].ArchivePath));
        Assert.All((await service.UndoAsync(plan.OperationId)).Entries, entry => Assert.Equal(CleanupEntryState.Restored, entry.State));
    }

    [Fact]
    public async Task CancellationAfterFirstMoveLeavesJournalAndUndoRestores()
    {
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        using CancellationTokenSource cancellation = new();
        CleanupResult result = await service.ExecuteAsync(plan, new ImmediateProgress(_ => cancellation.Cancel()), cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.Single(result.Entries, entry => entry.State == CleanupEntryState.Archived);
        Assert.Single(result.Entries, entry => entry.State == CleanupEntryState.OriginalPresent);
        Assert.All((await Service().UndoAsync(plan.OperationId)).Entries, entry => Assert.True(File.Exists(entry.Move.OriginalPath)));
    }

    [Fact]
    public async Task PartialFailureAccountsForAllEntriesAndNeverOverwritesCollision()
    {
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        CleanupMove second = plan.Moves[1];
        CleanupResult result = await service.ExecuteAsync(plan, progress: new ImmediateProgress(_ =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(second.ArchivePath)!);
            File.WriteAllText(second.ArchivePath, "external collision");
        }));
        Assert.Equal(CleanupEntryState.Archived, result.Entries[0].State);
        Assert.Equal(CleanupEntryState.Conflict, result.Entries[1].State);
        Assert.Equal("external collision", File.ReadAllText(second.ArchivePath));
        Assert.True(File.Exists(second.OriginalPath));
        CleanupResult undo = await Service().UndoAsync(plan.OperationId);
        Assert.Equal(CleanupEntryState.Restored, undo.Entries[0].State);
        Assert.Equal(CleanupEntryState.Conflict, undo.Entries[1].State);
    }

    [Fact]
    public async Task UndoNeverOverwritesRecreatedOriginal()
    {
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        await service.ExecuteAsync(plan);
        File.WriteAllText(plan.Moves[0].OriginalPath, "new original");
        CleanupResult result = await service.UndoAsync(plan.OperationId);
        Assert.Equal(CleanupEntryState.Conflict, result.Entries[0].State);
        Assert.Equal("new original", File.ReadAllText(plan.Moves[0].OriginalPath));
        Assert.True(File.Exists(plan.Moves[0].ArchivePath));
        Assert.Equal(CleanupEntryState.Restored, result.Entries[1].State);
    }

    [Fact]
    public async Task RecoveryInfersMovesEvenWhenLastStatusWriteWasInterrupted()
    {
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        CleanupResult result = await service.ExecuteAsync(plan);
        JsonObject journal = JsonNode.Parse(File.ReadAllText(result.JournalPath))!.AsObject();
        journal["Changed"] = false;
        File.WriteAllText(result.JournalPath, journal.ToJsonString());
        CleanupResult recovered = await Service().ReadRecoveryAsync(plan.OperationId);
        Assert.True(recovered.IndexRequiresRefresh);
        Assert.All(recovered.Entries, entry => Assert.Equal(CleanupEntryState.Archived, entry.State));
        Assert.All((await Service().UndoAsync(plan.OperationId)).Entries, entry => Assert.Equal(CleanupEntryState.Restored, entry.State));
    }

    [Fact]
    public async Task RecoveryRefusesAlteredMappingBeforeUndoTouchesFiles()
    {
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        CleanupResult result = await service.ExecuteAsync(plan);
        JsonObject journal = JsonNode.Parse(File.ReadAllText(result.JournalPath))!.AsObject();
        journal["Moves"]![0]!["OriginalPath"] = plan.Keepers[0].Path;
        File.WriteAllText(result.JournalPath, journal.ToJsonString());
        await Assert.ThrowsAsync<IOException>(() => Service().UndoAsync(plan.OperationId));
        Assert.True(File.Exists(plan.Keepers[0].Path));
        Assert.All(plan.Moves, move => Assert.True(File.Exists(move.ArchivePath)));
    }

    [Fact]
    public async Task ExpiredForeignAndRepeatedPlansAreRejected()
    {
        MutableClock clock = new();
        RecoverableSmartCleanupService service = Service(clock);
        SmartCleanupPlan plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ExecuteAsync(plan));
        clock.Now += TimeSpan.FromMinutes(11);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(plan));
        plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        await service.ExecuteAsync(plan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(plan));
    }

    [Fact]
    public async Task WorkspaceLeaseAndPreCancelledExecutionLeaveOriginalsUntouched()
    {
        RecoverableSmartCleanupService service = Service();
        SmartCleanupPlan plan = await service.PreviewAsync([Group()], KeeperRule.HighestResolution);
        using (WorkspaceLease lease = WorkspaceLease.Acquire(Path.Combine(_root, "data", "library.db"), Path.Combine(_root, "cache")))
            await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(plan));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecuteAsync(plan, cancellationToken: new CancellationToken(true)));
        Assert.All(plan.Moves, move => Assert.True(File.Exists(move.OriginalPath)));
    }

    [Fact]
    public void RefreshGuardPreservesOtherLibraryUntilItsCompleteScanAndMergesRoots()
    {
        string database = Path.Combine(_root, "data", "library.db");
        string first = Path.Combine(_root, "photos");
        string second = Path.Combine(_root, "other-photos");
        using WorkspaceLease lease = WorkspaceLease.Acquire(database, Path.Combine(_root, "cache"));
        IndexRefreshGuard.Mark(database, [first]);
        IndexRefreshGuard.Mark(database, [first + Path.DirectorySeparatorChar, second]);
        string marker = database + ".refresh-required";
        JsonObject contents = JsonNode.Parse(File.ReadAllText(marker))!.AsObject();
        Assert.Equal(1, contents["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(2, contents["LibraryRoots"]!.AsArray().Count);
        IndexRefreshGuard.CompletedScan(database, Path.Combine(_root, "unrelated"));
        Assert.True(File.Exists(marker));
        IndexRefreshGuard.CompletedScan(database, first);
        Assert.True(File.Exists(marker));
        Assert.Equal(second, JsonNode.Parse(File.ReadAllText(marker))!["LibraryRoots"]![0]!.GetValue<string>());
        IndexRefreshGuard.CompletedScan(database, second);
        Assert.False(File.Exists(marker));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"SchemaVersion\":2,\"LibraryRoots\":[]}")]
    [InlineData("{\"OperationId\":\"unknown\"}")]
    [InlineData("{\"SchemaVersion\":1,\"LibraryRoots\":null}")]
    public void RefreshGuardRefusesAndPreservesCorruptOrUnknownMarker(string contents)
    {
        string database = Path.Combine(_root, "data", "library.db");
        using WorkspaceLease lease = WorkspaceLease.Acquire(database, Path.Combine(_root, "cache"));
        string marker = database + ".refresh-required";
        File.WriteAllText(marker, contents);
        Assert.Throws<IOException>(() => IndexRefreshGuard.Mark(database, [Path.Combine(_root, "photos")]));
        Assert.Throws<IOException>(() => IndexRefreshGuard.CompletedScan(database, Path.Combine(_root, "photos")));
        Assert.Equal(contents, File.ReadAllText(marker));
    }

    [Fact]
    public void RefreshGuardRejectsDirectoryMarker()
    {
        string database = Path.Combine(_root, "data", "library.db");
        Directory.CreateDirectory(database + ".refresh-required");
        Assert.Throws<IOException>(() => IndexRefreshGuard.Mark(database, [Path.Combine(_root, "photos")]));
        Assert.Throws<IOException>(() => IndexRefreshGuard.CompletedScan(database, Path.Combine(_root, "photos")));
    }

    [Fact]
    public void RefreshGuardRejectsJunctionMarkerWithoutChangingTarget()
    {
        string database = Path.Combine(_root, "data", "library.db");
        string target = Path.Combine(_root, "junction-target");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        string marker = database + ".refresh-required";
        var junction = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        junction.ArgumentList.Add("/c");
        junction.ArgumentList.Add("mklink");
        junction.ArgumentList.Add("/J");
        junction.ArgumentList.Add(marker);
        junction.ArgumentList.Add(target);
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(junction)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.True((File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0);
        try
        {
            Assert.Throws<IOException>(() => IndexRefreshGuard.Mark(database, [Path.Combine(_root, "photos")]));
            Assert.Throws<IOException>(() => IndexRefreshGuard.CompletedScan(database, Path.Combine(_root, "photos")));
            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally { Directory.Delete(marker); }
    }

    [Fact]
    public void ArchiveCannotOverlapLibraryDatabaseCacheOrVolumeRoot()
    {
        foreach (string archive in new[] { Path.Combine(_root, "photos", "archive"), Path.Combine(_root, "data"),
            Path.Combine(_root, "cache", "archive"), Path.GetPathRoot(_root)! })
            Assert.Throws<IOException>(() => new RecoverableSmartCleanupService(Path.Combine(_root, "data", "library.db"),
                Path.Combine(_root, "cache"), [Path.Combine(_root, "photos")], archive));
    }

    private RecoverableSmartCleanupService Service(TimeProvider? time = null) => new(Path.Combine(_root, "data", "library.db"),
        Path.Combine(_root, "cache"), [Path.Combine(_root, "photos")], Path.Combine(_root, "archive"), time);

    private VerifiedCleanupGroup Group()
    {
        Directory.CreateDirectory(Path.Combine(_root, "photos"));
        List<CleanupFileCandidate> files = [];
        for (int index = 0; index < 3; index++)
        {
            string path = Path.Combine(_root, "photos", $"photo-{index}.jpg");
            File.WriteAllText(path, $"file-{index}");
            DateTime modified = new(2026, 1, 1 + index, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, modified);
            files.Add(new(path, index == 0 ? 4000 : 1000, 1000, File.GetLastWriteTimeUtc(path)));
        }
        return new(files.AsReadOnly(), true);
    }

    private sealed class ImmediateProgress(Action<CleanupEntryResult> callback) : IProgress<CleanupEntryResult>
    {
        public void Report(CleanupEntryResult value) => callback(value);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
