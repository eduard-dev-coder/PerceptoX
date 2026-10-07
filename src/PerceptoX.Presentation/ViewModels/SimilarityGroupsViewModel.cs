using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.Localization;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class GroupedImageViewModel(GroupedImageItem item) : ObservableObject
{
    public GroupedImageItem Item { get; } = item;
    public string Name => Item.FileName;
    public string Path => Item.FilePath;
    public Uri? Thumbnail => Item.ThumbnailUri;
    public string Metadata => Item.Metadata;
    public string Score => Item.ScoreLabel;
    public bool CanCompare => !Item.IsRepresentative;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Instance property is required for WinUI binding and live PropertyChanged notifications.")]
    public string CompareLabel => UiText.T("BatchMatchPage.Text039");
    public string GroupLabel => UiText.T("Grouping.GroupLabel", Item.GroupNumber, Item.GroupSize);
    public string Evidence => Item.IsRepresentative ? UiText.T("Grouping.Representative") : Item.BinaryIdentical
        ? UiText.T("Grouping.BinaryIdentical") : UiText.T("Grouping.CloseVariant");
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(GroupLabel)); OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(CompareLabel));
        OnPropertyChanged(nameof(Metadata)); OnPropertyChanged(nameof(Score));
    }
}

public sealed partial class SimilarityGroupsViewModel : LocalizedViewModel, IDisposable
{
    private readonly ISimilarityGroupingWorkflow? _workflow;
    private readonly IPathPickerService _picker;
    private readonly HashSet<long> _toggled = [];
    private bool _inverted, _refreshing, _disposed;
    private CancellationTokenSource? _operation;
    private WorkspaceConfiguration? _resultWorkspace;
    private GroupedImageItem? _representative;
    public GroupingAnalysisResult? LastRun { get; private set; }
    public WorkspaceViewModel Workspace { get; }
    public ObservableCollection<string> Folders { get; } = [];
    public ObservableCollection<GroupedImageViewModel> Groups { get; } = [];
    public ObservableCollection<GroupedImageViewModel> Members { get; } = [];

    public SimilarityGroupsViewModel(ISimilarityGroupingWorkflow? workflow, IPathPickerService picker, WorkspaceViewModel workspace)
    {
        _workflow = workflow; _picker = picker; Workspace = workspace;
        Workspace.PropertyChanged += WorkspaceChanged;
        SetStatus(() => UiText.T("Grouping.Ready"));
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; }
    public bool IsIdle => !IsBusy && !Workspace.IsOperationRunning;
    public bool CanAnalyze => IsIdle && !_disposed && _workflow is not null && Folders.Count > 0;
    public bool CanUseResults => IsIdle && !_disposed && LastRun is not null;
    [ObservableProperty] public partial bool EntireIndex { get; set; }
    [ObservableProperty] public partial double MinimumScore { get; set; } = 97;
    [ObservableProperty] public partial string FolderText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; private set; } = string.Empty;
    [ObservableProperty] public partial int SelectedCount { get; private set; }
    [ObservableProperty] public partial int GroupOffset { get; private set; }
    [ObservableProperty] public partial int MemberOffset { get; private set; }
    [ObservableProperty] public partial int CurrentGroup { get; private set; }
    [ObservableProperty] public partial string ActiveGroupLabel { get; private set; } = string.Empty;
    public string Summary => LastRun is { } run ? UiText.T("Grouping.Summary", run.Images, run.Groups, run.Members,
        run.Images - run.Members, run.Failed, run.Rejected) + (run.ExcludedImages > 0 ? UiText.T("Grouping.ExcludedProfiles", run.ExcludedImages) : "") : UiText.T("Grouping.Empty");
    public string SelectionLabel => UiText.T("Grouping.SelectionLabel", SelectedCount);
    public string PageLabel => LastRun is { } run ? UiText.T("Grouping.PageLabel", GroupOffset / 50 + 1,
        Math.Max(1, (run.Groups + 49) / 50)) : "";
    public string MemberPageLabel => _representative is { } item ? UiText.T("Grouping.PageLabel", MemberOffset / 50 + 1,
        (item.GroupSize + 49) / 50) : "";

    partial void OnIsBusyChanged(bool value) => RefreshAvailability();
    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(SelectionLabel));
    private void SetStatus(Func<string> render) => SetLocalized(nameof(StatusMessage), v => StatusMessage = v, render);
    private void RefreshAvailability()
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanAnalyze)); OnPropertyChanged(nameof(CanUseResults));
        AnalyzeCommand.NotifyCanExecuteChanged(); AddFolderCommand.NotifyCanExecuteChanged(); BrowseCommand.NotifyCanExecuteChanged();
        RemoveFolderCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
        SelectBestCommand.NotifyCanExecuteChanged(); InvertSelectionCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged(); ExportCsvCommand.NotifyCanExecuteChanged(); ExportHtmlCommand.NotifyCanExecuteChanged();
        PreviousGroupsCommand.NotifyCanExecuteChanged(); NextGroupsCommand.NotifyCanExecuteChanged();
        PreviousMembersCommand.NotifyCanExecuteChanged(); NextMembersCommand.NotifyCanExecuteChanged();
    }
    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        RefreshAvailability();
        if (_resultWorkspace is { } old && (old.DatabasePath != Workspace.DatabasePath || old.ThumbnailCacheRoot != Workspace.ThumbnailCacheRoot))
            InvalidateResults();
    }
    private bool CanWork() => CanAnalyze;
    private bool CanAct() => CanUseResults;
    private bool CanEdit() => IsIdle && !_disposed;
    private bool CanCancel() => IsBusy && _operation is not null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddFolder()
    {
        try
        {
            string full = Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(FolderText));
            if (!Folders.Contains(full, StringComparer.OrdinalIgnoreCase)) Folders.Add(full);
            FolderText = string.Empty; RefreshAvailability();
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { SetStatus(() => UiText.T("Grouping.Error", error.Message)); }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task BrowseAsync()
    {
        try { string? folder = await _picker.PickFolderAsync(CancellationToken.None); if (folder is not null) { FolderText = folder; AddFolder(); } }
        catch (Exception error) { SetStatus(() => UiText.T("Grouping.Error", error.Message)); }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RemoveFolder(string? folder) { if (folder is not null) Folders.Remove(folder); RefreshAvailability(); }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task AnalyzeAsync()
    {
        if (!CanAnalyze) return;
        if (!double.IsFinite(MinimumScore) || MinimumScore < 97 || MinimumScore > 100)
        { SetStatus(() => UiText.T("Grouping.InvalidThreshold")); return; }
        await RunAsync(async token =>
        {
            // A new indexing pass changes generations; never present the previous groups as current.
            InvalidateResults();
            var workspace = new WorkspaceConfiguration(Folders[0], Workspace.DatabasePath, Workspace.ThumbnailCacheRoot);
            long last = 0;
            var progress = new ThrottledUiProgress<int>(count =>
            {
                long now = Environment.TickCount64;
                if (now - last < 250) return;
                last = now; SetStatus(() => UiText.T("Grouping.Analyzing", count));
            }, token);
            var indexProgress = new ThrottledUiProgress<PerceptoX.Application.Indexing.IndexingProgress>(item =>
            {
                long now = Environment.TickCount64;
                if (now - last < 250) return;
                last = now; SetStatus(() => UiText.T("Grouping.Indexing", item.CurrentFileName, item.Processed + item.SkippedUnchanged));
            }, token);
            SetStatus(() => UiText.T("Grouping.Starting"));
            var result = await _workflow!.AnalyzeGroupsAsync(new(workspace with { Progress = indexProgress }, Folders.ToArray(), EntireIndex, MinimumScore, progress), token);
            ClearRows(); _toggled.Clear(); _inverted = false;
            LastRun = result; _resultWorkspace = workspace; SelectedCount = result.Groups;
            GroupOffset = 0; MemberOffset = 0; CurrentGroup = 0; _representative = null;
            var groups = await _workflow.LoadGroupPageAsync(workspace, result, 0, null, token);
            foreach (var row in groups) Groups.Add(new(row));
            if (groups.Count > 0) await LoadMembersCore(groups[0], 0, token);
            OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(PageLabel));
            SetStatus(() => Summary);
        });
    }

    [RelayCommand(CanExecute = nameof(CanCancel))] private void Cancel() => _operation?.Cancel();
    [RelayCommand(CanExecute = nameof(CanAct))] private void SelectBest()
    { _toggled.Clear(); _inverted = false; SelectedCount = LastRun!.Groups; RefreshSelections(); }
    [RelayCommand(CanExecute = nameof(CanAct))] private void InvertSelection()
    { _inverted = !_inverted; SelectedCount = LastRun!.Members - SelectedCount; RefreshSelections(); }

    public async Task ShowGroupAsync(GroupedImageViewModel row)
    {
        if (!CanUseResults) return;
        await RunAsync(token => LoadMembersCore(row.Item, 0, token));
    }
    private async Task LoadMembersCore(GroupedImageItem representative, int offset, CancellationToken token)
    {
        var rows = await _workflow!.LoadGroupPageAsync(_resultWorkspace!, LastRun!, offset, representative.GroupNumber, token);
        foreach (var old in Members) old.PropertyChanged -= MemberChanged;
        Members.Clear(); _representative = representative; CurrentGroup = representative.GroupNumber; MemberOffset = offset;
        ActiveGroupLabel = UiText.T("Grouping.GroupLabel", representative.GroupNumber, representative.GroupSize);
        foreach (var item in rows)
        {
            var row = new GroupedImageViewModel(item) { IsSelected = IsSelected(item) };
            row.PropertyChanged += MemberChanged; Members.Add(row);
        }
        OnPropertyChanged(nameof(MemberPageLabel));
    }
    private bool IsSelected(GroupedImageItem item) => item.IsRepresentative ^ _inverted ^ _toggled.Contains(item.ImageId);
    private void MemberChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_refreshing || args.PropertyName != nameof(GroupedImageViewModel.IsSelected) || sender is not GroupedImageViewModel row) return;
        if (!_toggled.Add(row.Item.ImageId)) _toggled.Remove(row.Item.ImageId);
        SelectedCount += row.IsSelected ? 1 : -1;
    }
    private void RefreshSelections()
    {
        _refreshing = true;
        try { foreach (var row in Members) row.IsSelected = IsSelected(row.Item); }
        finally { _refreshing = false; }
    }
    [RelayCommand(CanExecute = nameof(CanAct))] private Task PreviousGroupsAsync() => ChangeGroups(-50);
    [RelayCommand(CanExecute = nameof(CanAct))] private Task NextGroupsAsync() => ChangeGroups(50);
    private Task ChangeGroups(int delta)
    {
        int offset = GroupOffset + delta;
        if (offset < 0 || offset >= LastRun!.Groups) return Task.CompletedTask;
        return RunAsync(async token =>
        {
            var rows = await _workflow!.LoadGroupPageAsync(_resultWorkspace!, LastRun, offset, null, token);
            Groups.Clear(); foreach (var item in rows) Groups.Add(new(item)); GroupOffset = offset;
            OnPropertyChanged(nameof(PageLabel));
            if (rows.Count > 0) await LoadMembersCore(rows[0], 0, token);
        });
    }
    [RelayCommand(CanExecute = nameof(CanAct))] private Task PreviousMembersAsync() => ChangeMembers(-50);
    [RelayCommand(CanExecute = nameof(CanAct))] private Task NextMembersAsync() => ChangeMembers(50);
    private Task ChangeMembers(int delta)
    {
        int offset = MemberOffset + delta;
        return _representative is null || offset < 0 || offset >= _representative.GroupSize ? Task.CompletedTask
            : RunAsync(token => LoadMembersCore(_representative, offset, token));
    }
    [RelayCommand(CanExecute = nameof(CanAct))] private Task CopyAsync() => RunAsync(async token =>
    {
        if (SelectedCount == 0) return;
        string? destination = await _picker.PickFolderAsync(token); if (destination is null) return;
        var copied = await _workflow!.CopyGroupedAsync(_resultWorkspace!, LastRun!, new(_inverted, _toggled.ToHashSet()), destination, token);
        SetStatus(() => UiText.T("Grouping.Copied", copied.Copied, copied.Failed, copied.Cancelled));
    });
    [RelayCommand(CanExecute = nameof(CanAct))] private Task ExportCsvAsync() => ExportAsync(false);
    [RelayCommand(CanExecute = nameof(CanAct))] private Task ExportHtmlAsync() => ExportAsync(true);
    private Task ExportAsync(bool html) => RunAsync(async token =>
    {
        string? destination = await _picker.PickFolderAsync(token); if (destination is null) return;
        string path = await _workflow!.ExportGroupsAsync(_resultWorkspace!, LastRun!, new(_inverted, _toggled.ToHashSet()), destination, html, token);
        SetStatus(() => UiText.T("Grouping.Exported", path));
    });
    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (!IsIdle || _disposed) return;
        using CancellationTokenSource source = new(); _operation = source;
        Workspace.BeginExternalOperation(); IsBusy = true;
        try { await operation(source.Token); }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { SetStatus(() => UiText.T("Grouping.Cancelled")); }
        catch (Exception error) { SetStatus(() => UiText.T("Grouping.Error", error.Message)); }
        finally { source.Cancel(); _operation = null; IsBusy = false; Workspace.EndExternalOperation(); RefreshAvailability(); }
    }
    private void ClearRows()
    {
        foreach (var row in Members) row.PropertyChanged -= MemberChanged;
        Groups.Clear(); Members.Clear();
    }
    public void InvalidateResults()
    {
        ClearRows(); LastRun = null; _resultWorkspace = null; _representative = null; _toggled.Clear();
        SelectedCount = 0; CurrentGroup = 0; ActiveGroupLabel = string.Empty;
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(PageLabel)); OnPropertyChanged(nameof(MemberPageLabel));
        RefreshAvailability(); SetStatus(() => UiText.T("Grouping.Ready"));
    }
    public override void RefreshLanguage()
    {
        base.RefreshLanguage(); foreach (var row in Groups.Concat(Members)) row.RefreshLanguage();
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(SelectionLabel)); OnPropertyChanged(nameof(PageLabel)); OnPropertyChanged(nameof(MemberPageLabel));
        if (_representative is { } item) ActiveGroupLabel = UiText.T("Grouping.GroupLabel", item.GroupNumber, item.GroupSize);
    }
    public (string Reference, string Candidate)? ComparisonFor(GroupedImageViewModel row)
        => CanUseResults && _representative is { } representative && row.Item.ImageId != representative.ImageId
            ? (representative.FilePath, row.Item.FilePath) : null;
    public void ReportError(Exception error) => SetStatus(() => UiText.T("Grouping.Error", error.Message));
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _operation?.Cancel(); Workspace.PropertyChanged -= WorkspaceChanged; ClearRows();
    }
}
