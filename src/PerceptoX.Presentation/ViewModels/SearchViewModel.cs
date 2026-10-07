using PerceptoX.Presentation.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PerceptoX.Presentation.Services;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class SearchViewModel : LocalizedViewModel, IDisposable
{
    private void SetStatus(Func<string> render) => SetLocalized(nameof(StatusMessage), value => StatusMessage = value, render);
    private void AppendStatus(Func<string> render) => AppendLocalized(nameof(StatusMessage), value => StatusMessage = value, render);

    private readonly IPerceptoXWorkflow _workflow;
    private readonly IPathPickerService _pathPicker;
    private readonly SettingsViewModel _settings;
    private CancellationTokenSource? _operation;
    private readonly PropertyChangedEventHandler _workspaceChanged;

    public SearchViewModel(
        IPerceptoXWorkflow workflow,
        IPathPickerService pathPicker,
        WorkspaceViewModel workspace,
        SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(pathPicker);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(settings);
        _workflow = workflow;
        _pathPicker = pathPicker;
        Workspace = workspace;
        _settings = settings;
        _workspaceChanged = (_, _) => SearchCommand.NotifyCanExecuteChanged();
        Workspace.PropertyChanged += _workspaceChanged;
        SetStatus(() => UiText.T("SearchViewModel.Text001"));
    }

    public WorkspaceViewModel Workspace { get; }
    public ObservableCollection<SimilarImageItem> Results { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string SelectedImagePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = UiText.T("SearchViewModel.Text001");

    public bool HasResults => Results.Count != 0;

    private bool CanSearch() => !IsBusy && !Workspace.IsOperationRunning && Workspace.IsConfigured &&
        !string.IsNullOrWhiteSpace(SelectedImagePath);

    private bool CanCancel() => IsBusy;

    public async Task SearchDroppedImageAsync(string path)
    {
        if (IsBusy || Workspace.IsOperationRunning) { SetStatus(() => UiText.T("SearchViewModel.Text002")); return; }
        if (!Path.IsPathFullyQualified(path)) { SetStatus(() => UiText.T("SearchViewModel.Text003")); return; }
        SelectedImagePath = path;
        if (!CanSearch()) { SetStatus(() => UiText.T("SearchViewModel.Text004")); return; }
        await SearchCommand.ExecuteAsync(null);
    }

    public void ReportDropError(string message) => SetStatus(() => message);

    public void InvalidateResults(bool indexRemoved)
    {
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        SetStatus(() => indexRemoved ? UiText.T("SearchViewModel.Text005") :
            UiText.T("SearchViewModel.Text006"));
    }

    [RelayCommand]
    private async Task BrowseImageAsync()
    {
        string? selected = await _pathPicker.PickImageAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SelectedImagePath = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        IsBusy = true;
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        SetStatus(() => UiText.T("SearchViewModel.Text007"));
        try
        {
            _settings.Normalize();
            SimilarityQuery query = new(
                Workspace.CreateConfiguration(),
                SelectedImagePath,
                _settings.TopN,
                _settings.MaxPerceptualDistance,
                _settings.MaxDifferenceDistance,
                _settings.MaxMultiRegionDistance,
                _settings.MinimumMatchedRegions);
            IReadOnlyList<SimilarImageItem> matches = await _workflow.FindSimilarAsync(query, _operation.Token);
            foreach (SimilarImageItem match in matches.Take(1000))
            {
                Results.Add(match);
            }

            OnPropertyChanged(nameof(HasResults));
            SetStatus(() => UiText.T("SearchViewModel.Text008", Results.Count));
        }
        catch (OperationCanceledException) when (_operation.IsCancellationRequested)
        {
            SetStatus(() => UiText.T("SearchViewModel.Text009"));
        }
        catch (Exception exception)
        {
            SetStatus(() => UiText.T("BatchMatchViewModel.Text012", exception.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operation?.Cancel();

    public void Dispose()
    {
        Workspace.PropertyChanged -= _workspaceChanged;
        _operation?.Cancel();
        _operation?.Dispose();
    }
}
