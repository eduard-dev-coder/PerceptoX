using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.Presentation.Tests;

public sealed class BatchSelectionTests
{
    [Fact]
    public async Task SelectBestSkipsAmbiguousResultsAndCopiesUniqueOriginalPaths()
    {
        FakeActions actions = new();
        using BatchMatchViewModel vm = Create(actions);
        await vm.MatchCommand.ExecuteAsync(null);
        vm.SelectBestCommand.Execute(null);
        Assert.Equal(1, vm.SelectedCount);
        Assert.False(vm.Results[2].Candidates[0].IsSelected);
        vm.Results[2].Candidates[1].IsSelected = true;
        Assert.Equal(2, vm.SelectedCount);
        await vm.CopySelectedCommand.ExecuteAsync(null);
        Assert.Equal(["D:\\Originals\\shared.jpg", "D:\\Originals\\alternative.jpg"], actions.CopiedPaths);
        Assert.Equal("D:\\Destination", actions.Destination);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task RescanClearsSelectionAndExportUsesWholeSummary()
    {
        FakeActions actions = new();
        using BatchMatchViewModel vm = Create(actions);
        await vm.MatchCommand.ExecuteAsync(null);
        vm.SelectBestCommand.Execute(null);
        await vm.ExportHtmlCommand.ExecuteAsync(null);
        Assert.Equal(3, actions.Report!.TotalQueries);
        Assert.True(actions.Html);
        await vm.MatchCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.CopySelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task CopyFailureRestoresCommandsAndRetainsSelection()
    {
        using BatchMatchViewModel vm = Create(new FakeActions { Fail = true });
        await vm.MatchCommand.ExecuteAsync(null);
        vm.SelectBestCommand.Execute(null);
        await vm.CopySelectedCommand.ExecuteAsync(null);
        Assert.False(vm.IsBusy);
        Assert.True(vm.CopySelectedCommand.CanExecute(null));
        Assert.Contains("Access denied", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("D:\\Originals")]
    [InlineData("D:\\Originals\\Nested")]
    [InlineData("D:\\")]
    public void OverlappingRootsCannotStartScan(string query)
    {
        using BatchMatchViewModel vm = Create(new FakeActions());
        vm.QueryRoot = query;
        Assert.False(vm.MatchCommand.CanExecute(null));
    }

    private static BatchMatchViewModel Create(FakeActions actions) => new(
        new FakeWorkflow(), new FakePicker(), new WorkspaceViewModel { LibraryRoot = "D:\\Originals" },
        new SettingsViewModel(), actions) { QueryRoot = "D:\\Queries" };

    private sealed class FakePicker : IPathPickerService
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>("D:\\Destination");
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class FakeActions : IBatchResultActions
    {
        public bool Fail { get; init; }
        public IReadOnlyList<string>? CopiedPaths { get; private set; }
        public string? Destination { get; private set; }
        public BatchMatchSummary? Report { get; private set; }
        public bool Html { get; private set; }
        public Task<CopyOutcome> CopyAsync(IReadOnlyList<string> sourcePaths, string destination, CancellationToken cancellationToken)
        {
            if (Fail) throw new UnauthorizedAccessException("Access denied");
            CopiedPaths = sourcePaths;
            Destination = destination;
            return Task.FromResult(new CopyOutcome(sourcePaths.Count, 0, false, []));
        }
        public Task<string> ExportAsync(BatchMatchSummary summary, string destination, bool html, CancellationToken cancellationToken)
        {
            Report = summary;
            Html = html;
            return Task.FromResult(Path.Combine(destination, "report.html"));
        }
    }

    private sealed class FakeWorkflow : IPerceptoXWorkflow
    {
        public Task<IndexingSummary> IndexAsync(WorkspaceConfiguration workspace, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SimilarImageItem>> FindSimilarAsync(SimilarityQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BatchMatchSummary> MatchFolderAsync(BatchMatchRequest request, CancellationToken cancellationToken)
        {
            SimilarImageItem first = new(1, "D:\\Originals\\shared.jpg", null, 99, 1, 1, 0, 3);
            SimilarImageItem alternative = new(2, "D:\\Originals\\alternative.jpg", null, 98, 2, 2, 1, 3);
            BatchMatchItem Row(BatchMatchStatus status) => new("D:\\Queries\\query.jpg", status, 1,
                first.FilePath, null, 99, 2, 1, 1, 0, null) { Candidates = [first, alternative] };
            return Task.FromResult(new BatchMatchSummary(new(1, 2, 0, 2, 0, 0), 3, 1, 1, 1, 0, 0,
                [Row(BatchMatchStatus.Found), Row(BatchMatchStatus.NearIdentical), Row(BatchMatchStatus.Ambiguous)]));
        }
    }
}
