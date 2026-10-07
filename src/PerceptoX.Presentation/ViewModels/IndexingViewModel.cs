using PerceptoX.Presentation.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PerceptoX.Presentation.Services;
using System.ComponentModel;
using PerceptoX.Application.Indexing;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class IndexingViewModel : LocalizedViewModel, IDisposable
{
    private void SetStatus(Func<string> render) => SetLocalized(nameof(StatusMessage), value => StatusMessage = value, render);
    private void AppendStatus(Func<string> render) => AppendLocalized(nameof(StatusMessage), value => StatusMessage = value, render);

    private readonly IPerceptoXWorkflow _workflow;
    private readonly IPathPickerService _pathPicker;
    private CancellationTokenSource? _operation;
    private readonly PropertyChangedEventHandler _workspaceChanged;

    public IndexingViewModel(
        IPerceptoXWorkflow workflow,
        IPathPickerService pathPicker,
        WorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(pathPicker);
        ArgumentNullException.ThrowIfNull(workspace);
        _workflow = workflow;
        _pathPicker = pathPicker;
        Workspace = workspace;
        _workspaceChanged = (_, _) => IndexCommand.NotifyCanExecuteChanged();
        Workspace.PropertyChanged += _workspaceChanged;
        SetStatus(() => UiText.T("IndexingViewModel.Text001"));
    }

    public WorkspaceViewModel Workspace { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = UiText.T("IndexingViewModel.Text001");

    [ObservableProperty]
    public partial IndexingSummary? LastSummary { get; private set; }

    [ObservableProperty]
    public partial string ProgressLabel { get; private set; } = string.Empty;

    private bool CanIndex() => !IsBusy && !Workspace.IsOperationRunning && Workspace.IsConfigured;
    private bool CanCancel() => IsBusy;

    [RelayCommand]
    private async Task BrowseLibraryAsync()
    {
        string? selected = await _pathPicker.PickFolderAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            Workspace.LibraryRoot = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanIndex))]
    private async Task IndexAsync()
    {
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        IsBusy = true;
        LastSummary = null;
        SetStatus(() => UiText.T("IndexingViewModel.Text002"));
        SetLocalized(nameof(ProgressLabel), value => ProgressLabel = value, () => string.Empty);
        try
        {
            LastSummary = await _workflow.IndexAsync(
                Workspace.CreateConfiguration() with { Progress = new Progress<IndexingProgress>(progress =>
                    SetLocalized(nameof(ProgressLabel), value => ProgressLabel = value, () => UiText.T("IndexingViewModel.Text003", progress.Processed, progress.SkippedUnchanged, progress.Failed, progress.Corrupt, progress.Unsupported, progress.Inaccessible, progress.ImagesPerSecond))) }, _operation.Token);
            SetStatus(() => LastSummary.Failed == 0
                ? UiText.T("IndexingViewModel.Text004", LastSummary.Processed, LastSummary.SkippedUnchanged)
                : UiText.T("IndexingViewModel.Text005", LastSummary.Failed));
            if (!LastSummary.DiscoveryComplete) AppendStatus(() => UiText.T("IndexingViewModel.Text006"));
            if (LastSummary.Unsupported > 0) AppendStatus(() => UiText.T("IndexingViewModel.Text007", LastSummary.Unsupported));
        }
        catch (OperationCanceledException) when (_operation.IsCancellationRequested)
        {
            SetStatus(() => UiText.T("IndexingViewModel.Text008"));
        }
        catch (Exception exception)
        {
            SetStatus(() => UiText.T("IndexingViewModel.Text009", exception.Message));
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
