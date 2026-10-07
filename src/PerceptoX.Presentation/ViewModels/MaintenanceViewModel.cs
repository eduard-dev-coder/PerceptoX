using PerceptoX.Presentation.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PerceptoX.Application.Maintenance;
using PerceptoX.Presentation.Services;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class MaintenanceViewModel : LocalizedViewModel, IDisposable
{
    private void SetStatus(Func<string> render) => SetLocalized(nameof(StatusMessage), value => StatusMessage = value, render);
    private void AppendStatus(Func<string> render) => AppendLocalized(nameof(StatusMessage), value => StatusMessage = value, render);

    private readonly IMaintenanceService _service;
    private readonly IConfirmationService _confirmation;
    private readonly IPathPickerService _picker;
    private readonly WorkspaceViewModel _workspace;
    private readonly Func<IReadOnlyList<string>> _protectedRoots;
    private readonly Func<bool> _workspaceIdle;
    private readonly Action<bool> _invalidateResults;
    private CancellationTokenSource? _operation;

    public MaintenanceViewModel(IMaintenanceService service, IConfirmationService confirmation,
        IPathPickerService picker, WorkspaceViewModel workspace, Func<IReadOnlyList<string>> protectedRoots,
        Func<bool> workspaceIdle, Action<bool> invalidateResults)
    {
        _service = service;
        _confirmation = confirmation;
        _picker = picker;
        _workspace = workspace;
        _protectedRoots = protectedRoots;
        _workspaceIdle = workspaceIdle;
        _invalidateResults = invalidateResults;
        SetStatus(() => UiText.T("MaintenanceViewModel.Text002"));
        SetLocalized(nameof(StorageLabel), value => StorageLabel = value, () => UiText.T("MaintenanceViewModel.Text001"));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCacheCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearReportsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StorageLabel { get; private set; } = UiText.T("MaintenanceViewModel.Text001");

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = UiText.T("MaintenanceViewModel.Text002");

    private bool CanStart() => !IsBusy && _workspaceIdle();
    private bool CanCancel() => IsBusy;

    public void RefreshAvailability()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ClearCacheCommand.NotifyCanExecuteChanged();
        ResetIndexCommand.NotifyCanExecuteChanged();
        ClearReportsCommand.NotifyCanExecuteChanged();
    }

    private MaintenanceRequest Request(MaintenanceLevel level, string? reportRoot = null) =>
        new(_workspace.DatabasePath, _workspace.ThumbnailCacheRoot, _protectedRoots(), level, reportRoot)
        {
            Progress = new Progress<MaintenanceProgress>(value =>
            {
                if (IsBusy) SetStatus(() => UiText.T("MaintenanceViewModel.Text003", value.Deleted, value.Total, value.FreedBytes / 1048576d));
            })
        };

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task RefreshAsync() => RunAsync(async token =>
    {
        await MeasureAsync(token);
        SetStatus(() => UiText.T("MaintenanceViewModel.Text004"));
    });

    private async Task MeasureAsync(CancellationToken token)
    {
        StorageUsage usage = await _service.MeasureAsync(Request(MaintenanceLevel.Cache), token);
        SetLocalized(nameof(StorageLabel), value => StorageLabel = value, () => UiText.T("MaintenanceViewModel.Text005", usage.DatabaseBytes / 1048576d, usage.CacheBytes / 1048576d, usage.CacheFiles));
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task ClearCacheAsync() => CleanAsync(MaintenanceLevel.Cache);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task ResetIndexAsync() => CleanAsync(MaintenanceLevel.IndexAndCache);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task ClearReportsAsync() => CleanAsync(MaintenanceLevel.Reports);

    private Task CleanAsync(MaintenanceLevel level) => RunAsync(async token =>
    {
        string? destination = level == MaintenanceLevel.Reports ? await _picker.PickFolderAsync(token) : null;
        if (level == MaintenanceLevel.Reports && string.IsNullOrWhiteSpace(destination)) return;
        MaintenancePlan plan = await _service.PreviewAsync(Request(level, destination), token);
        if (plan.Files.Count == 0) { SetStatus(() => UiText.T("MaintenanceViewModel.Text006")); return; }
        string title = level switch
        {
            MaintenanceLevel.Cache => UiText.T("SettingsPage.Text040"),
            MaintenanceLevel.IndexAndCache => UiText.T("MaintenanceViewModel.Text007"),
            _ => UiText.T("MaintenanceViewModel.Text008")
        };
        string effect = level switch
        {
            MaintenanceLevel.Cache => UiText.T("MaintenanceViewModel.Text009"),
            MaintenanceLevel.IndexAndCache => UiText.T("MaintenanceViewModel.Text010"),
            _ => UiText.T("MaintenanceViewModel.Text011")
        };
        string targets = level == MaintenanceLevel.Reports ? destination! :
            UiText.T("MaintenanceViewModel.Text012", plan.Request.CacheRoot) + (level == MaintenanceLevel.IndexAndCache ? UiText.T("MaintenanceViewModel.Text013", plan.Request.DatabasePath) : "");
        string libraries = level == MaintenanceLevel.IndexAndCache ? UiText.T("MaintenanceViewModel.Text014") + string.Join('\n', plan.AffectedLibraries) : "";
        string message = UiText.T("MaintenanceViewModel.Text015", plan.Files.Count, plan.Bytes / 1048576d, targets, libraries, effect);
        if (plan.PreservedFiles > 0) message += UiText.T("MaintenanceViewModel.Text016", plan.PreservedFiles);
        if (!await _confirmation.ConfirmAsync(title, message, token)) { SetStatus(() => UiText.T("MaintenanceViewModel.Text017")); return; }
        token.ThrowIfCancellationRequested();
        // A workflow may have started while the confirmation was open.
        if (!_workspaceIdle()) throw new IOException(UiText.T("MaintenanceViewModel.Text018"));
        MaintenanceResult result = await _service.ExecuteAsync(plan, token);
        if (level != MaintenanceLevel.Reports && (result.Deleted > 0 || result.IndexRemoved)) _invalidateResults(result.IndexRemoved);
        SetStatus(() => UiText.T("MaintenanceViewModel.Text019", result.Deleted, result.FreedBytes / 1048576d));
        if (result.Cancelled) AppendStatus(() => UiText.T("MaintenanceViewModel.Text020"));
        if (result.Errors.Count > 0) AppendStatus(() => "\n" + string.Join('\n', result.Errors));
        await MeasureAsync(CancellationToken.None);
    });

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        IsBusy = true;
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { SetStatus(() => UiText.T("BatchMatchViewModel.Text021")); }
        catch (Exception exception) { SetStatus(() => UiText.T("MaintenanceViewModel.Text021", exception.Message)); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operation?.Cancel();

    public void Dispose() { _operation?.Cancel(); _operation?.Dispose(); }
}
