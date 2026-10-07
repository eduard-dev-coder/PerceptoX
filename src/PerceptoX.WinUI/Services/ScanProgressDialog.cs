using PerceptoX.Presentation.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.WinUI.Services;

internal static class ScanProgressDialog
{
    internal static async Task ShowAsync(BatchMatchViewModel model, FrameworkElement owner)
    {
        if (!model.MatchCommand.CanExecute(null)) return;
        TextBlock stage = new() { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        TextBlock file = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        TextBlock counts = new() { TextWrapping = TextWrapping.Wrap, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        TextBlock details = new() { TextWrapping = TextWrapping.Wrap };
        TextBlock elapsed = new(), eta = new() { TextWrapping = TextWrapping.Wrap };
        TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        ProgressBar progress = new() { Minimum = 0, Maximum = 100, IsIndeterminate = true };
        StackPanel content = new() { Spacing = 12, Width = Math.Clamp(owner.XamlRoot.Size.Width - 160, 260, 520) };
        content.Children.Add(DialogLayout.Section(UiText.T("ScanProgressDialog.Text001"), stage, file));
        content.Children.Add(progress);
        Border processingCard = DialogLayout.Section(UiText.T("ScanProgressDialog.Text002"), counts, details);
        content.Children.Add(processingCard);
        Grid times = new() { ColumnSpacing = 16 };
        times.ColumnDefinitions.Add(new ColumnDefinition()); times.ColumnDefinitions.Add(new ColumnDefinition());
        times.Children.Add(elapsed); Grid.SetColumn(eta, 1); times.Children.Add(eta);
        content.Children.Add(DialogLayout.Section(UiText.T("ScanProgressDialog.Text003"), times));
        Border summaryCard = DialogLayout.Section(UiText.T("ScanProgressDialog.Text004"), summary);
        summaryCard.Visibility = Visibility.Collapsed;
        content.Children.Add(summaryCard);
        ContentDialog dialog = new PerceptoXDialog()
        {
            XamlRoot = owner.XamlRoot, Title = UiText.T("BatchMatchPage.Text011"), PrimaryButtonText = UiText.T("ModuleManagerDialog.Text004"),
            DefaultButton = ContentDialogButton.None,
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Clamp(owner.XamlRoot.Size.Height - 200, 180, 560), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        bool detached = false;
        Task? operation = null;
        void Render()
        {
            if (detached) return;
            model.ScanProgress.Refresh();
            ScanProgressSnapshot state = model.ScanProgress.Snapshot;
            stage.Text = state.StageLabel;
            file.Text = UiText.T("ScanProgressDialog.Text005") + state.FileName;
            file.Visibility = state.HasFile ? Visibility.Visible : Visibility.Collapsed;
            counts.Text = state.CountsLabel; details.Text = state.DetailsLabel;
            processingCard.Visibility = state.IsRunning || state.Total.HasValue ? Visibility.Visible : Visibility.Collapsed;
            elapsed.Text = state.ElapsedLabel; eta.Text = state.EtaLabel;
            eta.Visibility = state.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            summary.Text = state.Summary;
            summaryCard.Visibility = string.IsNullOrWhiteSpace(state.Summary) ? Visibility.Collapsed : Visibility.Visible;
            progress.IsIndeterminate = state.IsIndeterminate; progress.Value = state.Percent;
            progress.Visibility = state.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            dialog.PrimaryButtonText = state.IsRunning ? UiText.T("ModuleManagerDialog.Text004") : UiText.T("ImageComparisonDialog.Text004");
            dialog.IsPrimaryButtonEnabled = !state.IsRunning || !state.CancellationRequested;
            dialog.DefaultButton = state.IsRunning ? ContentDialogButton.None : ContentDialogButton.Primary;
            if (!state.IsRunning && state.Outcome != ScanOutcome.Ready) timer.Stop();
        }
        async Task RunAsync()
        {
            try { await model.MatchCommand.ExecuteAsync(null); }
            catch (Exception error) { model.ScanProgress.Complete(ScanOutcome.Failed, UiText.T("ScanProgressDialog.Text006") + error.Message); }
            finally { Render(); }
        }
        void Opened(ContentDialog _, ContentDialogOpenedEventArgs __) { timer.Start(); operation = RunAsync(); }
        void Tick(object? _, object __) => Render();
        void PrimaryClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
        {
            if (!model.ScanProgress.Snapshot.IsRunning) return;
            args.Cancel = true;
            model.CancelCommand.Execute(null);
            Render();
        }
        void Closing(ContentDialog _, ContentDialogClosingEventArgs args)
        {
            // Escape/click-outside must not dismiss an active background operation.
            args.Cancel = model.ScanProgress.Snapshot.IsRunning;
        }
        void Detach()
        {
            if (detached) return;
            detached = true;
            timer.Stop(); timer.Tick -= Tick;
            dialog.Opened -= Opened; dialog.PrimaryButtonClick -= PrimaryClick; dialog.Closing -= Closing;
            owner.Unloaded -= Unloaded;
        }
        void Unloaded(object _, RoutedEventArgs __)
        {
            if (model.ScanProgress.Snapshot.IsRunning) model.CancelCommand.Execute(null);
            Detach();
            dialog.Hide();
        }
        timer.Tick += Tick; dialog.Opened += Opened; dialog.PrimaryButtonClick += PrimaryClick;
        dialog.Closing += Closing; owner.Unloaded += Unloaded;
        try
        {
            // Start the engine only after the dialog is open, including instant/empty scans.
            await dialog.ShowAsync();
            if (operation is not null) await operation;
        }
        finally
        {
            if (model.ScanProgress.Snapshot.IsRunning) model.CancelCommand.Execute(null);
            Detach();
            dialog.Content = null;
            if (operation is not null) await operation;
        }
    }
}
