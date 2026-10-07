using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
using PerceptoX.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using PerceptoX.WinUI.Services;

namespace PerceptoX.WinUI.Pages;

public sealed partial class BatchMatchPage : Page
{
    public BatchMatchPage() => InitializeComponent();
    private bool _scanDialogOpen;
    private async void OnMatchClick(object sender, RoutedEventArgs e)
    {
        if (_scanDialogOpen || DataContext is not BatchMatchViewModel model || !model.CanStartMatch) return;
        _scanDialogOpen = true;
        try { await ScanProgressDialog.ShowAsync(model, this); }
        catch (Exception error) { model.ReportDialogFailure(error); }
        finally { _scanDialogOpen = false; }
    }
    private async void OnSmartCleanupClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is BatchMatchViewModel model && !model.Workspace.IsOperationRunning)
            await SmartCleanupDialog.ShowAsync(model, XamlRoot, recover: false);
    }
    private async void OnRecoverCleanupClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is BatchMatchViewModel model && !model.Workspace.IsOperationRunning)
            await SmartCleanupDialog.ShowAsync(model, XamlRoot, recover: true);
    }

    private async void OnCompareClick(object sender, RoutedEventArgs e)
    {
        await CompareAsync(sender);
    }

    private async void OnCompareDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        await CompareAsync(sender);
    }

    private async Task CompareAsync(object sender)
    {
        if (sender is not FrameworkElement { DataContext: MatchCandidateViewModel candidate } ||
            DataContext is not BatchMatchViewModel model || model.Workspace.IsOperationRunning) return;
        model.Workspace.BeginExternalOperation();
        try { await ImageComparisonDialog.ShowAsync(candidate.QueryPath, candidate.Image.FilePath, this); }
        catch (Exception error) { model.ReportDialogFailure(error); }
        finally { model.Workspace.EndExternalOperation(); }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DataContext = e.Parameter as BatchMatchViewModel;
        if (new UISettings().AnimationsEnabled)
        {
            DoubleAnimation fade = new() { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(180) };
            Storyboard.SetTarget(fade, ContentRoot);
            Storyboard.SetTargetProperty(fade, "Opacity");
            Storyboard storyboard = new();
            storyboard.Children.Add(fade);
            storyboard.Begin();
        }
    }
}
