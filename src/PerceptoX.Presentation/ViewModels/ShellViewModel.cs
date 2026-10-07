using PerceptoX.Presentation.Services;
using PerceptoX.Application.Maintenance;
using System.ComponentModel;
using PerceptoX.Application.Calibration;

namespace PerceptoX.Presentation.ViewModels;

public sealed class ShellViewModel : IDisposable
{
    public ShellViewModel(IPerceptoXWorkflow workflow, IPathPickerService pathPicker,
        IBatchResultActions? batchActions = null, IMaintenanceService? maintenance = null,
        IConfirmationService? confirmation = null, ICalibrationStore? calibration = null,
        string? processingProfile = null, ISimilarityGroupingWorkflow? grouping = null)
    {
        Workspace = new WorkspaceViewModel();
        Settings = new SettingsViewModel();
        BatchMatch = new BatchMatchViewModel(workflow, pathPicker, Workspace, Settings, batchActions);
        Indexing = new IndexingViewModel(workflow, pathPicker, Workspace);
        Search = new SearchViewModel(workflow, pathPicker, Workspace, Settings);
        Groups = new SimilarityGroupsViewModel(grouping, pathPicker, Workspace);
        BatchMatch.LibraryChanged += OnLibraryChanged;
        Workspace.PropertyChanged += OnWorkspaceChanged;
        if (calibration is not null && processingProfile is not null)
            Settings.Calibration = new CalibrationViewModel(calibration, Settings, processingProfile,
                () => !Workspace.IsOperationRunning);
        if (maintenance is not null && confirmation is not null)
        {
            Settings.Maintenance = new MaintenanceViewModel(maintenance, confirmation, pathPicker, Workspace,
                () => new[] { Workspace.LibraryRoot, BatchMatch.QueryRoot },
                () => !Workspace.IsExternalOperationRunning && !BatchMatch.IsBusy && !Indexing.IsBusy && !Search.IsBusy,
                reset => { BatchMatch.InvalidateResults(); Search.InvalidateResults(reset); Groups.InvalidateResults(); });
            BatchMatch.PropertyChanged += OnOperationChanged;
            Indexing.PropertyChanged += OnOperationChanged;
            Search.PropertyChanged += OnOperationChanged;
            Settings.Maintenance.PropertyChanged += OnOperationChanged;
        }
    }

    public WorkspaceViewModel Workspace { get; }
    public BatchMatchViewModel BatchMatch { get; }
    public IndexingViewModel Indexing { get; }
    public SearchViewModel Search { get; }
    public SimilarityGroupsViewModel Groups { get; }
    public SettingsViewModel Settings { get; }

    public void RefreshLanguage()
    {
        BatchMatch.RefreshLanguage(); Indexing.RefreshLanguage(); Search.RefreshLanguage();
        Settings.Maintenance?.RefreshLanguage(); Settings.Calibration?.RefreshLanguage();
        Settings.RefreshLanguage();
        Groups.RefreshLanguage();
        foreach (BatchResultViewModel row in BatchMatch.Results) row.RefreshLanguage();
    }

    private void OnOperationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "IsBusy") return;
        if (sender is IndexingViewModel { IsBusy: false } || sender is BatchMatchViewModel { IsBusy: false }) Groups.InvalidateResults();
        Workspace.IsOperationRunning = Workspace.IsExternalOperationRunning || BatchMatch.IsBusy || Indexing.IsBusy || Search.IsBusy || Settings.Maintenance?.IsBusy == true;
        Settings.Maintenance?.RefreshAvailability();
    }
    private void OnLibraryChanged(object? sender, EventArgs args) { Search.InvalidateResults(true); Groups.InvalidateResults(); }
    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Workspace.IsOperationRunning)) Settings.Maintenance?.RefreshAvailability();
    }

    public void Dispose()
    {
        BatchMatch.LibraryChanged -= OnLibraryChanged;
        Workspace.PropertyChanged -= OnWorkspaceChanged;
        BatchMatch.PropertyChanged -= OnOperationChanged;
        Indexing.PropertyChanged -= OnOperationChanged;
        Search.PropertyChanged -= OnOperationChanged;
        if (Settings.Maintenance is { } maintenance)
        {
            maintenance.PropertyChanged -= OnOperationChanged;
            maintenance.Dispose();
        }
        BatchMatch.Dispose();
        Indexing.Dispose();
        Search.Dispose();
        Groups.Dispose();
    }
}
