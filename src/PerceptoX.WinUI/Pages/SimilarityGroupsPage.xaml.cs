using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PerceptoX.Presentation.ViewModels;
using PerceptoX.WinUI.Services;

namespace PerceptoX.WinUI.Pages;

public sealed partial class SimilarityGroupsPage : Page
{
    private bool _dialogOpen;
    public SimilarityGroupsPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs e)
    { base.OnNavigatedTo(e); DataContext = e.Parameter as SimilarityGroupsViewModel; }
    private void RemoveFolderClick(object sender, RoutedEventArgs args)
    {
        if (DataContext is SimilarityGroupsViewModel model && sender is FrameworkElement { DataContext: string path })
            model.RemoveFolderCommand.Execute(path);
    }
    private async void GroupChanged(object sender, SelectionChangedEventArgs args)
    {
        if (DataContext is SimilarityGroupsViewModel model && GroupList.SelectedItem is GroupedImageViewModel row)
            await model.ShowGroupAsync(row);
    }
    private async void CompareClick(object sender, RoutedEventArgs args)
    {
        if (_dialogOpen || DataContext is not SimilarityGroupsViewModel model ||
            sender is not FrameworkElement { DataContext: GroupedImageViewModel row } || model.ComparisonFor(row) is not { } pair) return;
        _dialogOpen = true;
        try { await ImageComparisonDialog.ShowAsync(pair.Reference, pair.Candidate, this); }
        catch (Exception error) { model.ReportError(error); }
        finally { _dialogOpen = false; }
    }
}
