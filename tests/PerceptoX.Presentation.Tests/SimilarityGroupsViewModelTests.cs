using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.Presentation.Tests;

public sealed class SimilarityGroupsViewModelTests
{
    private sealed class Picker : IPathPickerService
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>("D:\\Output");
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
    private sealed class Workflow : ISimilarityGroupingWorkflow
    {
        public GroupingSelection? Copied { get; private set; }
        public bool Fail { get; set; }
        public bool WaitForCancellation { get; set; }
        public async Task<GroupingAnalysisResult> AnalyzeGroupsAsync(GroupingAnalysisRequest request, CancellationToken token)
        {
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, token);
            if (Fail) throw new IOException("Test failure");
            return new("test", 4, 1, 4, 97, 0, 0, []);
        }
        public Task<IReadOnlyList<GroupedImageItem>> LoadGroupPageAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
            int offset, int? groupNumber, CancellationToken token)
        {
            var items = Enumerable.Range(1, groupNumber is null ? 1 : 4).Select(id => new GroupedImageItem(1, id,
                id == 1, 100, id != 1, "D:\\Photos\\" + id + ".png", 100, 100, 1234, 0, null, 4)).ToArray();
            return Task.FromResult<IReadOnlyList<GroupedImageItem>>(items);
        }
        public Task<CopyOutcome> CopyGroupedAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
            GroupingSelection selection, string destination, CancellationToken token)
        { Copied = selection; return Task.FromResult(new CopyOutcome(1, 0, false, [])); }
        public Task<string> ExportGroupsAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
            GroupingSelection selection, string destination, bool html, CancellationToken token) => Task.FromResult("D:\\report.csv");
    }
    private static SimilarityGroupsViewModel Create(Workflow workflow)
    {
        var vm = new SimilarityGroupsViewModel(workflow, new Picker(), new WorkspaceViewModel());
        vm.FolderText = "D:\\Photos"; vm.AddFolderCommand.Execute(null);
        return vm;
    }

    [Fact]
    public async Task DefaultSelectsOnlyRepresentativeAndInverseSelectsRemainingMembers()
    {
        using var vm = Create(new());
        await vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.SelectedCount); Assert.True(vm.Members[0].IsSelected);
        Assert.All(vm.Members.Skip(1), item => Assert.False(item.IsSelected));
        vm.InvertSelectionCommand.Execute(null);
        Assert.Equal(3, vm.SelectedCount); Assert.False(vm.Members[0].IsSelected);
        Assert.All(vm.Members.Skip(1), item => Assert.True(item.IsSelected));
        vm.InvertSelectionCommand.Execute(null); Assert.Equal(1, vm.SelectedCount);
    }

    [Fact]
    public async Task ManualSelectionSurvivesPageReloadAndCopyUsesWholeSelectionState()
    {
        Workflow workflow = new(); using var vm = Create(workflow);
        await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.Members[2].IsSelected = true;
        await vm.ShowGroupAsync(vm.Groups[0]);
        Assert.True(vm.Members[2].IsSelected); Assert.Equal(2, vm.SelectedCount);
        await vm.CopyCommand.ExecuteAsync(null);
        Assert.True(workflow.Copied!.Includes(1, true)); Assert.True(workflow.Copied.Includes(3, false));
        Assert.False(workflow.Copied.Includes(2, false)); Assert.False(vm.Workspace.IsOperationRunning);
    }

    [Fact]
    public async Task InvertingManualSelectionIsAnInvolution()
    {
        using var vm = Create(new()); await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.Members[1].IsSelected = true;
        bool[] initial = vm.Members.Select(item => item.IsSelected).ToArray();
        vm.InvertSelectionCommand.Execute(null);
        Assert.Equal(initial.Select(value => !value), vm.Members.Select(item => item.IsSelected));
        vm.InvertSelectionCommand.Execute(null);
        Assert.Equal(initial, vm.Members.Select(item => item.IsSelected));
        vm.SelectBestCommand.Execute(null); Assert.Equal(1, vm.SelectedCount);
    }

    [Fact]
    public async Task FailureAndCancellationReleaseSharedWorkspaceGate()
    {
        Workflow workflow = new() { Fail = true }; using var vm = Create(workflow);
        await vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.False(vm.IsBusy); Assert.True(vm.CanAnalyze); Assert.Null(vm.LastRun);
        workflow.Fail = false; workflow.WaitForCancellation = true;
        Task operation = vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.True(vm.Workspace.IsOperationRunning); Assert.True(vm.IsBusy);
        vm.CancelCommand.Execute(null); await operation;
        Assert.False(vm.Workspace.IsOperationRunning); Assert.False(vm.IsBusy); Assert.True(vm.CanAnalyze);
    }

    [Fact]
    public async Task DatabaseChangeInvalidatesSelectionAndResults()
    {
        using var vm = Create(new()); await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.Workspace.DatabasePath = "D:\\Other\\index.db";
        Assert.Null(vm.LastRun); Assert.Empty(vm.Members); Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.CopyCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(96)] [InlineData(101)] [InlineData(double.NaN)]
    public async Task UnsupportedThresholdDoesNotStartOrAcquireWorkspace(double threshold)
    {
        using var vm = Create(new()); vm.MinimumScore = threshold;
        await vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.False(vm.Workspace.IsOperationRunning); Assert.Null(vm.LastRun);
    }
}
