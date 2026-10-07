using PerceptoX.Application.Indexing;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.Presentation.Tests;

public sealed class ScanProgressTests
{
    private static IndexingProgress Progress(int completed, int? total = null, string stage = "Processing") =>
        new(1, total ?? completed, 0, completed, 0, 0, 0, 2, stage) { Total = total, CurrentFileName = "current.png" };

    [Fact]
    public void UnknownTotalHasNoFabricatedRemainingOrEta()
    {
        ScanProgressViewModel model = new(); model.Begin(); model.Report(Progress(4)); model.Refresh();
        Assert.True(model.Snapshot.IsRunning); Assert.True(model.Snapshot.IsIndeterminate);
        Assert.Null(model.Snapshot.Eta); Assert.Null(model.Snapshot.Total);
        Assert.Equal("current.png", model.Snapshot.FileName);
    }

    [Fact]
    public void WorkerBurstIsCoalescedWithoutPropertyEventsUntilUiRefresh()
    {
        ScanProgressViewModel model = new(); model.Begin();
        int events = 0; model.PropertyChanged += (_, _) => events++;
        for (int i = 1; i <= 10000; i++) model.Report(Progress(i, 10000));
        Assert.Equal(0, events); Assert.Equal(0, model.Snapshot.Completed);
        model.Refresh(); Assert.Equal(1, events); Assert.Equal(10000, model.Snapshot.Completed);
        Assert.Equal(100, model.Snapshot.Percent);
    }

    [Fact]
    public void KnownTotalUsesPhaseRateAndResetsEtaForQueries()
    {
        ManualClock clock = new(); ScanProgressViewModel model = new(clock); model.Begin();
        model.Report(Progress(0, 100, "Discovery")); clock.Advance(40);
        model.Report(Progress(25, 100)); model.Refresh();
        Assert.Equal(TimeSpan.FromSeconds(120), model.Snapshot.Eta);
        Assert.Equal(25, model.Snapshot.Percent);
        model.Report(Progress(0, 10, "Loading") with { Activity = "Queries" }); clock.Advance(10);
        model.Report(Progress(1, 10, "Search") with { Activity = "Queries" }); model.Refresh();
        Assert.Equal(TimeSpan.FromSeconds(90), model.Snapshot.Eta);
        Assert.Equal(TimeSpan.FromSeconds(50), model.Snapshot.Elapsed);
    }

    [Theory]
    [InlineData(ScanOutcome.Succeeded)]
    [InlineData(ScanOutcome.Cancelled)]
    [InlineData(ScanOutcome.Failed)]
    public void TerminalStatesAreFrozenAndIgnoreLateReports(ScanOutcome outcome)
    {
        ManualClock clock = new(); ScanProgressViewModel model = new(clock); model.Begin();
        model.Report(Progress(3, 10)); clock.Advance(4); model.Complete(outcome, "summary");
        ScanProgressSnapshot final = model.Snapshot;
        model.Report(Progress(9, 10)); clock.Advance(10); model.Refresh();
        Assert.Equal(final, model.Snapshot); Assert.False(final.IsRunning); Assert.False(final.HasFile);
        Assert.Equal("summary", final.Summary); Assert.Equal(3, final.Completed);
    }

    [Fact]
    public void RequestingCancellationDoesNotPretendRollbackHasFinished()
    {
        ScanProgressViewModel model = new(); model.Begin(); model.RequestCancellation();
        Assert.Equal(ScanOutcome.Running, model.Snapshot.Outcome);
        Assert.True(model.Snapshot.CancellationRequested);
        model.Complete(ScanOutcome.Cancelled, "published data retained");
        Assert.Equal(ScanOutcome.Cancelled, model.Snapshot.Outcome);
    }

    [Fact]
    public async Task BatchSuccessFinishesWithSummaryAndLateProgressCannotReopenIt()
    {
        IProgress<IndexingProgress>? observer = null;
        using BatchMatchViewModel model = Create(async (request, _) =>
        {
            observer = request.Workspace.Progress;
            observer!.Report(Progress(2, 2)); await Task.Yield(); return Summary();
        });
        await model.MatchCommand.ExecuteAsync(null);
        Assert.Equal(ScanOutcome.Succeeded, model.ScanProgress.Snapshot.Outcome);
        Assert.Contains("Originale", model.ScanProgress.Snapshot.Summary, StringComparison.Ordinal);
        observer!.Report(Progress(999)); model.ScanProgress.Refresh();
        Assert.Equal(2, model.ScanProgress.Snapshot.Completed); Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task BatchCancellationWaitsForBackendCleanupBeforeTerminalState()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using BatchMatchViewModel model = Create(async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { await cleanup.Task; throw; }
            return Summary();
        });
        Task task = model.MatchCommand.ExecuteAsync(null); await entered.Task;
        model.CancelCommand.Execute(null);
        Assert.True(model.IsBusy); Assert.True(model.ScanProgress.Snapshot.IsRunning);
        Assert.False(task.IsCompleted); cleanup.SetResult(); await task;
        Assert.False(model.IsBusy); Assert.Equal(ScanOutcome.Cancelled, model.ScanProgress.Snapshot.Outcome);
    }

    [Fact]
    public async Task BatchFailureHasTerminalErrorAndReleasesBusyState()
    {
        using BatchMatchViewModel model = Create((_, _) => throw new InvalidOperationException("SQLite fault"));
        await model.MatchCommand.ExecuteAsync(null);
        Assert.Equal(ScanOutcome.Failed, model.ScanProgress.Snapshot.Outcome);
        Assert.Contains("SQLite fault", model.ScanProgress.Snapshot.Summary, StringComparison.Ordinal);
        Assert.True(model.CanStartMatch);
        Assert.Equal(1, model.ScanProgress.Snapshot.Errors);
    }

    [Fact]
    public async Task DisposingActiveBatchCancelsAndUnsubscribesWorkspaceEvents()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BatchMatchViewModel model = Create(async (_, token) =>
        {
            entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return Summary();
        });
        Task operation = model.MatchCommand.ExecuteAsync(null); await entered.Task;
        model.Dispose(); await operation;
        Assert.Equal(ScanOutcome.Cancelled, model.ScanProgress.Snapshot.Outcome);
        int changed = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.CanStartMatch)) changed++; };
        model.Workspace.LibraryRoot = "D:\\DifferentOriginals";
        Assert.Equal(0, changed); Assert.False(model.CanStartMatch);
        model.Dispose();
    }

    private static BatchMatchViewModel Create(Func<BatchMatchRequest, CancellationToken, Task<BatchMatchSummary>> run) =>
        new(new Workflow(run), new Picker(), new WorkspaceViewModel { LibraryRoot = "D:\\Originals" }, new SettingsViewModel()) { QueryRoot = "D:\\Queries" };
    private static BatchMatchSummary Summary() => new(new(1, 2, 0, 2, 0, 0), 0, 0, 0, 0, 0, 0, []);
    private sealed class Workflow(Func<BatchMatchRequest, CancellationToken, Task<BatchMatchSummary>> run) : IPerceptoXWorkflow
    {
        public Task<BatchMatchSummary> MatchFolderAsync(BatchMatchRequest request, CancellationToken token) => run(request, token);
        public Task<IndexingSummary> IndexAsync(WorkspaceConfiguration workspace, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<SimilarImageItem>> FindSimilarAsync(SimilarityQuery query, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Picker : IPathPickerService
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(int seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
