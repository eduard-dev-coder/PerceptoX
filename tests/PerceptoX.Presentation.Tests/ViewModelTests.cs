using System.Globalization;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.Presentation.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public async Task IndexCommandRequiresConfigurationAndReportsSuccess()
    {
        FakeWorkflow workflow = new();
        WorkspaceViewModel workspace = new();
        IndexingViewModel viewModel = new(workflow, new FakePicker(), workspace);
        Assert.False(viewModel.IndexCommand.CanExecute(null));
        workspace.LibraryRoot = "D:\\Photos";
        Assert.True(viewModel.IndexCommand.CanExecute(null));

        await viewModel.IndexCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.NotNull(viewModel.LastSummary);
        Assert.Contains("terminată", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexCancellationIsPassedToWorkflowAndRestoresIdleState()
    {
        FakeWorkflow workflow = new(waitForCancellation: true);
        WorkspaceViewModel workspace = ConfiguredWorkspace();
        IndexingViewModel viewModel = new(workflow, new FakePicker(), workspace);

        Task operation = viewModel.IndexCommand.ExecuteAsync(null);
        await workflow.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        viewModel.CancelCommand.Execute(null);
        await operation;

        Assert.False(viewModel.IsBusy);
        Assert.Contains("anulată", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchNormalizesSettingsAndBoundsDisplayedResults()
    {
        FakeWorkflow workflow = new(resultCount: 1200);
        WorkspaceViewModel workspace = ConfiguredWorkspace();
        SettingsViewModel settings = new() { TopN = 5000, MaxMultiRegionDistance = 80 };
        SearchViewModel viewModel = new(workflow, new FakePicker(), workspace, settings)
        {
            SelectedImagePath = "D:\\Photos\\selected.jpg"
        };

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(1000, workflow.LastQuery!.TopN);
        Assert.Equal(64, workflow.LastQuery.MaxMultiRegionDistance);
        Assert.Equal(1000, viewModel.Results.Count);
        Assert.True(viewModel.HasResults);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SearchShowsUserReadableFailureAndLeavesNoStaleResults()
    {
        FakeWorkflow workflow = new(failure: new KeyNotFoundException("Imaginea nu este în index."));
        SearchViewModel viewModel = new(workflow, new FakePicker(), ConfiguredWorkspace(), new SettingsViewModel())
        {
            SelectedImagePath = "D:\\Photos\\missing.jpg"
        };

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Results);
        Assert.Contains("nu este în index", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData("en-US", "93.25")]
    [InlineData("ro-RO", "93,25")]
    public void SimilarImageItemProvidesAConciseAccessibleSummary(string culture, string expectedScore)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            SimilarImageItem item = new(42, "D:\\Photos\\sample.jpg", null,
                93.25, 3, 4, 2, 7);

            Assert.Equal(
                $"sample.jpg, similaritate {expectedScore} procente, p=3/64 · d=4/64 · mr=2/64 · 7/9 regiuni",
                item.AccessibilityLabel);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task BatchMatchIndexesAndSummarizesTwoSeparateFolders()
    {
        FakeWorkflow workflow = new();
        WorkspaceViewModel workspace = ConfiguredWorkspace();
        BatchMatchViewModel viewModel = new(workflow, new FakePicker(), workspace, new SettingsViewModel())
        {
            QueryRoot = "D:\\Queries"
        };

        await viewModel.MatchCommand.ExecuteAsync(null);

        Assert.NotNull(workflow.LastBatchRequest);
        Assert.Same(workspace, viewModel.Workspace);
        Assert.Single(viewModel.Results);
        Assert.Contains("1/1 găsite", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    private static WorkspaceViewModel ConfiguredWorkspace() => new()
    {
        LibraryRoot = "D:\\Photos",
        DatabasePath = "D:\\PerceptoX\\index.db",
        ThumbnailCacheRoot = "D:\\PerceptoX\\thumbs"
    };

    private sealed class FakePicker : IPathPickerService
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class FakeWorkflow(
        int resultCount = 0,
        bool waitForCancellation = false,
        Exception? failure = null) : IPerceptoXWorkflow
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SimilarityQuery? LastQuery { get; private set; }
        public BatchMatchRequest? LastBatchRequest { get; private set; }

        public async Task<IndexingSummary> IndexAsync(
            WorkspaceConfiguration workspace,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (waitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (failure is not null)
            {
                throw failure;
            }

            return new IndexingSummary(1, 10, 2, 8, 0, 0);
        }

        public Task<IReadOnlyList<SimilarImageItem>> FindSimilarAsync(
            SimilarityQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            if (failure is not null)
            {
                throw failure;
            }

            IReadOnlyList<SimilarImageItem> results = Enumerable.Range(1, resultCount)
                .Select(index => new SimilarImageItem(index, $"D:\\Photos\\{index}.jpg", null,
                    90, 1, 1, null, 0))
                .ToArray();
            return Task.FromResult(results);
        }

        public Task<BatchMatchSummary> MatchFolderAsync(
            BatchMatchRequest request,
            CancellationToken cancellationToken)
        {
            LastBatchRequest = request;
            BatchMatchItem item = new(
                Path.Combine(request.QueryRoot, "query.jpg"),
                BatchMatchStatus.NearIdentical,
                1,
                Path.Combine(request.Workspace.LibraryRoot, "original.jpg"),
                null,
                99,
                1,
                0,
                0,
                0,
                null);
            return Task.FromResult(new BatchMatchSummary(
                new IndexingSummary(1, 1, 0, 1, 0, 0),
                1, 0, 1, 0, 0, 0, [item]));
        }
    }
}
