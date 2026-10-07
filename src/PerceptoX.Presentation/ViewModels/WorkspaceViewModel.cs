using PerceptoX.Presentation.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    public partial bool IsOperationRunning { get; internal set; }

    public bool IsEditable => !IsOperationRunning;
    public bool IsExternalOperationRunning { get; private set; }
    public void BeginExternalOperation()
    {
        if (IsOperationRunning) throw new InvalidOperationException(UiText.T("SearchViewModel.Text002"));
        IsExternalOperationRunning = true;
        IsOperationRunning = true;
    }
    public void EndExternalOperation()
    {
        IsExternalOperationRunning = false;
        IsOperationRunning = false;
    }
    [ObservableProperty]
    public partial string LibraryRoot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DatabasePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerceptoX",
        "index.db");

    [ObservableProperty]
    public partial string ThumbnailCacheRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerceptoX",
        "thumbs");

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(LibraryRoot) &&
        !string.IsNullOrWhiteSpace(DatabasePath) &&
        !string.IsNullOrWhiteSpace(ThumbnailCacheRoot);

    public Services.WorkspaceConfiguration CreateConfiguration()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(UiText.T("WorkspaceViewModel.Text001"));
        }

        return new Services.WorkspaceConfiguration(LibraryRoot, DatabasePath, ThumbnailCacheRoot);
    }

    partial void OnLibraryRootChanged(string value) => OnPropertyChanged(nameof(IsConfigured));
    partial void OnDatabasePathChanged(string value) => OnPropertyChanged(nameof(IsConfigured));
    partial void OnThumbnailCacheRootChanged(string value) => OnPropertyChanged(nameof(IsConfigured));
}
