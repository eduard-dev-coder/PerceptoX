using PerceptoX.Presentation.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PerceptoX.Presentation.Services;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class BatchMatchViewModel : LocalizedViewModel, IDisposable
{
    private void SetStatus(Func<string> render) => SetLocalized(nameof(StatusMessage), value => StatusMessage = value, render);
    private void AppendStatus(Func<string> render) => AppendLocalized(nameof(StatusMessage), value => StatusMessage = value, render);

    private readonly IPerceptoXWorkflow _workflow;
    private readonly IPathPickerService _pathPicker;
    private readonly SettingsViewModel _settings;
    private readonly IBatchResultActions? _actions;
    private readonly PropertyChangedEventHandler _workspaceChanged;
    private CancellationTokenSource? _operation;
    private bool _bulkSelection;
    private bool _disposed;

    public BatchMatchViewModel(
        IPerceptoXWorkflow workflow,
        IPathPickerService pathPicker,
        WorkspaceViewModel workspace,
        SettingsViewModel settings,
        IBatchResultActions? actions = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(pathPicker);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(settings);
        _workflow = workflow;
        _pathPicker = pathPicker;
        Workspace = workspace;
        _settings = settings;
        _actions = actions;
        _workspaceChanged = (_, _) =>
        {
            MatchCommand.NotifyCanExecuteChanged();
            CopySelectedCommand.NotifyCanExecuteChanged();
            ExportCsvCommand.NotifyCanExecuteChanged();
            ExportHtmlCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanStartMatch));
        };
        Workspace.PropertyChanged += _workspaceChanged;
        SetStatus(() => UiText.T("BatchMatchViewModel.Text005"));
    }

    public WorkspaceViewModel Workspace { get; }
    public event EventHandler? LibraryChanged;
    public void NotifyLibraryChanged()
    {
        InvalidateResults();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ReportDialogFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        SetStatus(() => UiText.T("BatchMatchViewModel.Text001") + error.Message);
    }
    public ObservableCollection<BatchResultViewModel> Results { get; } = [];
    public SettingsViewModel Settings => _settings;
    public bool IsIdle => !IsBusy;
    public bool CanStartMatch => CanMatch();
    public bool HasResults => Results.Count > 0;
    public bool HasActionDetails => !string.IsNullOrWhiteSpace(ActionDetails);
    public string SummaryLabel => LastSummary is null ? UiText.T("BatchMatchPage.Text025") :
        UiText.T("BatchMatchViewModel.Text002", LastSummary.TotalQueries, LastSummary.Found + LastSummary.NearIdentical) +
        UiText.T("BatchMatchViewModel.Text003", LastSummary.NearIdentical, LastSummary.Ambiguous, LastSummary.NotFound, LastSummary.Failed);

    [ObservableProperty]
    public partial int SearchProfileIndex { get; set; }

    partial void OnSearchProfileIndexChanged(int value)
    {
        (_settings.MaxPerceptualDistance, _settings.MaxDifferenceDistance,
            _settings.MaxMultiRegionDistance, _settings.MinimumMatchedRegions) = value switch
        {
            1 => (6, 6, 2, 2),
            2 => (10, 12, 4, 1),
            _ => (2, 2, 1, 1)
        };
    }
    public string EmptyMessage => Results.Count == 0 ? UiText.T("BatchMatchViewModel.Text004") : string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    public partial int SelectedCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionDetails))]
    public partial string ActionDetails { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MatchCommand))]
    [NotifyPropertyChangedFor(nameof(CanStartMatch))]
    public partial string QueryRoot { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportHtmlCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanStartMatch))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial ScanProgressViewModel ScanProgress { get; private set; } = new();

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } =
        UiText.T("BatchMatchViewModel.Text005");

    [ObservableProperty]
    public partial string ProgressLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLabel))]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportHtmlCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    public partial BatchMatchSummary? LastSummary { get; private set; }

    private bool CanMatch()
    {
        if (_disposed || IsBusy || Workspace.IsOperationRunning || !Workspace.IsConfigured || string.IsNullOrWhiteSpace(QueryRoot))
        {
            return false;
        }

        try
        {
            string query = Path.TrimEndingDirectorySeparator(Path.GetFullPath(QueryRoot));
            string library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Workspace.LibraryRoot));
            return !IsSameOrNested(query, library) && !IsSameOrNested(library, query);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private bool CanCancel() => IsBusy;
    private bool CanCopy() => !IsBusy && !Workspace.IsOperationRunning && _actions is not null && SelectedCount > 0;
    private bool CanExport() => !IsBusy && !Workspace.IsOperationRunning && _actions is not null && LastSummary is not null;
    private bool CanSelect() => !IsBusy && LastSummary is not null;

    public void InvalidateResults()
    {
        UnsubscribeCandidates();
        Results.Clear();
        LastSummary = null;
        SelectedCount = 0;
        ActionDetails = string.Empty;
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(EmptyMessage));
        SetStatus(() => UiText.T("BatchMatchViewModel.Text006"));
    }

    private static bool IsSameOrNested(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    [RelayCommand]
    private async Task BrowseQueryFolderAsync()
    {
        string? selected = await _pathPicker.PickFolderAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            QueryRoot = selected;
        }
    }

    [RelayCommand]
    private async Task BrowseLibraryFolderAsync()
    {
        string? selected = await _pathPicker.PickFolderAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            Workspace.LibraryRoot = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanMatch))]
    private async Task MatchAsync()
    {
        if (!CanMatch()) return;
        _operation?.Dispose();
        using CancellationTokenSource operation = new();
        _operation = operation;
        ScanProgress = new ScanProgressViewModel();
        ScanProgress.Begin();
        ScanOutcome outcome = ScanOutcome.Failed;
        IsBusy = true;
        LastSummary = null;
        UnsubscribeCandidates();
        Results.Clear();
        SelectedCount = 0;
        ActionDetails = string.Empty;
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(HasResults));
        SetStatus(() => UiText.T("BatchMatchViewModel.Text007"));
        ProgressLabel = string.Empty;
        try
        {
            _settings.Normalize();
            BatchMatchRequest request = new(
                Workspace.CreateConfiguration() with { Progress = ScanProgress },
                QueryRoot,
                TopCandidates: 3,
                _settings.MaxPerceptualDistance,
                _settings.MaxDifferenceDistance,
                _settings.MaxMultiRegionDistance,
                _settings.MinimumMatchedRegions);
            LastSummary = await _workflow.MatchFolderAsync(request, operation.Token);
            foreach (BatchMatchItem item in LastSummary.Items)
            {
                BatchResultViewModel row = new(item);
                foreach (MatchCandidateViewModel candidate in row.Candidates)
                    candidate.PropertyChanged += OnCandidateChanged;
                Results.Add(row);
            }

            SetStatus(() => UiText.T("BatchMatchViewModel.Text008", LastSummary.Found + LastSummary.NearIdentical, LastSummary.TotalQueries) +
                UiText.T("BatchMatchViewModel.Text009", LastSummary.NearIdentical, LastSummary.Ambiguous) +
                UiText.T("BatchMatchViewModel.Text010", LastSummary.NotFound, LastSummary.Failed));
            outcome = ScanOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetStatus(() => UiText.T("BatchMatchViewModel.Text011"));
            outcome = ScanOutcome.Cancelled;
        }
        catch (Exception exception)
        {
            SetStatus(() => UiText.T("BatchMatchViewModel.Text012", exception.Message));
        }
        finally
        {
            string summary = StatusMessage;
            if (LastSummary is { } result)
                summary += UiText.T("BatchMatchViewModel.Text013", result.Indexing.Processed, result.Indexing.SkippedUnchanged) +
                    UiText.T("BatchMatchViewModel.Text014", result.Indexing.Failed, result.Indexing.Unsupported, result.Indexing.Inaccessible);
            ScanProgress.Complete(outcome, summary + UiText.T("BatchMatchViewModel.Text015"));
            ProgressLabel = ScanProgress.Snapshot.CountsLabel;
            if (ReferenceEquals(_operation, operation)) _operation = null;
            IsBusy = false;
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(HasResults));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void SelectBest()
    {
        ClearSelection();
        _bulkSelection = true;
        foreach (BatchResultViewModel row in Results)
            if (row.Item.Status is BatchMatchStatus.Found or BatchMatchStatus.NearIdentical && row.Candidates.Count > 0)
                row.Candidates[0].IsSelected = true;
        _bulkSelection = false;
        RefreshSelectionCount();
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void ClearSelection()
    {
        _bulkSelection = true;
        foreach (MatchCandidateViewModel candidate in Results.SelectMany(row => row.Candidates))
            candidate.IsSelected = false;
        _bulkSelection = false;
        RefreshSelectionCount();
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private Task CopySelectedAsync() => RunResultActionAsync(async (destination, token) =>
    {
        string[] paths = Results.SelectMany(row => row.Candidates).Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Image.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        CopyOutcome result = await _actions!.CopyAsync(paths, destination, token);
        SetStatus(() => UiText.T("BatchMatchViewModel.Text016", result.Copied, result.Failed) +
            (result.Cancelled ? UiText.T("BatchMatchViewModel.Text017") : UiText.T("BatchMatchViewModel.Text018")));
        ActionDetails = destination + (result.Errors.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, result.Errors));
    });

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportCsvAsync() => ExportAsync(false);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportHtmlAsync() => ExportAsync(true);

    private Task ExportAsync(bool html) => RunResultActionAsync(async (destination, token) =>
    {
        string path = await _actions!.ExportAsync(LastSummary!, destination, html, token);
        SetStatus(() => UiText.T("BatchMatchViewModel.Text019", (html ? "HTML" : "CSV")));
        ActionDetails = path;
    });

    private async Task RunResultActionAsync(Func<string, CancellationToken, Task> action)
    {
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        IsBusy = true;
        ActionDetails = string.Empty;
        try
        {
            string? destination = await _pathPicker.PickFolderAsync(_operation.Token);
            if (string.IsNullOrWhiteSpace(destination)) return;
            SetStatus(() => UiText.T("BatchMatchViewModel.Text020"));
            await action(destination, _operation.Token);
        }
        catch (OperationCanceledException) { SetStatus(() => UiText.T("BatchMatchViewModel.Text021")); }
        catch (Exception exception) { SetStatus(() => UiText.T("BatchMatchViewModel.Text022", exception.Message)); }
        finally { IsBusy = false; }
    }

    private void OnCandidateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_bulkSelection && args.PropertyName == nameof(MatchCandidateViewModel.IsSelected))
            RefreshSelectionCount();
    }

    private void RefreshSelectionCount() =>
        SelectedCount = Results.SelectMany(row => row.Candidates).Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Image.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    private void UnsubscribeCandidates()
    {
        foreach (MatchCandidateViewModel candidate in Results.SelectMany(row => row.Candidates))
            candidate.PropertyChanged -= OnCandidateChanged;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (ScanProgress.Snapshot.IsRunning) ScanProgress.RequestCancellation();
        _operation?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Workspace.PropertyChanged -= _workspaceChanged;
        UnsubscribeCandidates();
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = null;
    }
}
