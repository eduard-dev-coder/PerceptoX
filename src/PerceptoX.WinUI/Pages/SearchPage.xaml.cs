using PerceptoX.Presentation.Localization;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PerceptoX.Presentation.ViewModels;
using PerceptoX.WinUI.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace PerceptoX.WinUI.Pages;

public sealed partial class SearchPage : Page
{
    public SearchPage() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = DataPackageOperation.None;
        if (DataContext is not SearchViewModel { IsBusy: false } viewModel ||
            viewModel.Workspace.IsOperationRunning || !args.DataView.Contains(StandardDataFormats.StorageItems)) return;
        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = UiText.T("SearchPage.xaml.Text001");
    }

    private async void OnDrop(object sender, DragEventArgs args)
    {
        if (DataContext is not SearchViewModel viewModel) return;
        var deferral = args.GetDeferral();
        try
        {
            var items = await args.DataView.GetStorageItemsAsync();
            if (items.Count != 1 || items[0] is not StorageFile file)
            { viewModel.ReportDropError(UiText.T("SearchPage.xaml.Text002")); return; }
            await viewModel.SearchDroppedImageAsync(file.Path);
        }
        catch (Exception exception) { viewModel.ReportDropError(UiText.T("SearchPage.xaml.Text003", exception.Message)); }
        finally { deferral.Complete(); }
    }

#if DEBUG
    private bool _profileStarted;
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };
#endif

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DataContext = e.Parameter as SearchViewModel;

#if DEBUG
        if (DataContext is SearchViewModel viewModel && UxProfileOptions.ReportPath is not null)
        {
            viewModel.Results.CollectionChanged += OnResultsChanged;
            Unloaded += (_, _) => viewModel.Results.CollectionChanged -= OnResultsChanged;
            TryStartProfile(viewModel);
        }
#endif
    }

#if DEBUG
    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is SearchViewModel viewModel)
        {
            TryStartProfile(viewModel);
        }
    }

    private void TryStartProfile(SearchViewModel viewModel)
    {
        if (_profileStarted || viewModel.Results.Count < UxProfileOptions.ExpectedResultCount)
        {
            return;
        }

        _profileStarted = true;
        _ = RunUxProfileSafelyAsync(viewModel);
    }

    private static string ReportPath => UxProfileOptions.ReportPath!;

    private async Task RunUxProfileSafelyAsync(SearchViewModel viewModel)
    {
        try
        {
            await RunUxProfileAsync(viewModel);
        }
        catch (Exception exception)
        {
            EnsureReportDirectory();
            await File.WriteAllTextAsync(ReportPath,
                JsonSerializer.Serialize(new
                {
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    Error = exception.ToString(),
                    MeetsEngineeringThresholds = false
                }, ReportJsonOptions));
        }
    }

    private async Task RunUxProfileAsync(SearchViewModel viewModel)
    {
        await Task.Delay(750);
        long workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        List<double> frameLatencyMilliseconds = [];
        int maximumRealizedContainers = 0;
        int[] targets = [0, 100, 250, 500, 750, 999, 750, 500, 250, 0];

        foreach (int target in targets)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ResultsGrid.ScrollIntoView(viewModel.Results[target], ScrollIntoViewAlignment.Leading);
            await WaitForNextRenderingAsync();
            stopwatch.Stop();
            frameLatencyMilliseconds.Add(stopwatch.Elapsed.TotalMilliseconds);
            maximumRealizedContainers = Math.Max(maximumRealizedContainers,
                ResultsGrid.ItemsPanelRoot?.Children.Count ?? 0);
            await Task.Delay(100);
        }

        double[] ordered = frameLatencyMilliseconds.Order().ToArray();
        int percentileIndex = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);
        ResultsGrid.ScrollIntoView(viewModel.Results[0], ScrollIntoViewAlignment.Leading);
        await WaitForNextRenderingAsync();
        GridViewItem? firstContainer = ResultsGrid.ContainerFromIndex(0) as GridViewItem;
        bool firstItemAcceptedKeyboardFocus = firstContainer?.Focus(FocusState.Keyboard) == true;
        bool rightNavigationSucceeded = firstItemAcceptedKeyboardFocus &&
            FocusManager.TryMoveFocus(FocusNavigationDirection.Right,
                new FindNextElementOptions { SearchRoot = ResultsGrid });
        await Task.Delay(150);
        int focusedResultIndex = -1;
        for (int index = 0; index < Math.Min(10, viewModel.Results.Count); index++)
        {
            if (ResultsGrid.ContainerFromIndex(index) is GridViewItem { FocusState: not FocusState.Unfocused })
            {
                focusedResultIndex = index;
                break;
            }
        }
        UxProfileReport report = new(
            DateTimeOffset.UtcNow,
            viewModel.Results.Count,
            targets.Length,
            ordered[percentileIndex],
            ordered[^1],
            maximumRealizedContainers,
            Process.GetCurrentProcess().WorkingSet64 - workingSetBefore,
            ActualTheme.ToString(),
            XamlRoot?.RasterizationScale ?? 0,
            ActualWidth,
            ActualHeight,
            frameLatencyMilliseconds,
            firstItemAcceptedKeyboardFocus,
            rightNavigationSucceeded,
            focusedResultIndex,
            viewModel.Results.Count >= UxProfileOptions.ExpectedResultCount &&
            maximumRealizedContainers < 200 && ordered[percentileIndex] < 100 &&
            firstItemAcceptedKeyboardFocus && rightNavigationSucceeded && focusedResultIndex > 0);

        EnsureReportDirectory();
        await File.WriteAllTextAsync(ReportPath,
            JsonSerializer.Serialize(report, ReportJsonOptions));
    }

    private static void EnsureReportDirectory()
    {
        string? reportDirectory = Path.GetDirectoryName(ReportPath);
        if (!string.IsNullOrWhiteSpace(reportDirectory))
        {
            Directory.CreateDirectory(reportDirectory);
        }
    }

    private static async Task WaitForNextRenderingAsync()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            completion.TrySetResult();
        };
        CompositionTarget.Rendering += handler;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            CompositionTarget.Rendering -= handler;
        }
    }

    private sealed record UxProfileReport(
        DateTimeOffset CapturedAtUtc,
        int ResultCount,
        int ScrollSamples,
        double P95NextFrameMilliseconds,
        double MaximumNextFrameMilliseconds,
        int MaximumRealizedContainers,
        long WorkingSetDeltaBytes,
        string Theme,
        double RasterizationScale,
        double ViewportWidth,
        double ViewportHeight,
        IReadOnlyList<double> NextFrameMilliseconds,
        bool FirstItemAcceptedKeyboardFocus,
        bool RightNavigationSucceeded,
        int FocusedResultIndexAfterRightNavigation,
        bool MeetsEngineeringThresholds);
#endif
}
