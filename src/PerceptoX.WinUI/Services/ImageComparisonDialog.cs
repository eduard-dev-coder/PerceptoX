using PerceptoX.Presentation.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Presentation.Services;

namespace PerceptoX.WinUI.Services;

internal static class ImageComparisonDialog
{
    internal static async Task ShowAsync(string reference, string candidate, FrameworkElement owner)
    {
        using CancellationTokenSource cancellation = new();
        ComparisonPreview? preview = null;
        ComparisonImagePane? left = null, right = null;
        ScrollViewer? active = null;
        RelativeImagePoint? position = null;
        bool closed = false;
        double contentWidth = Math.Clamp(owner.XamlRoot.Size.Width - 160, 300, 1050);
        StackPanel content = new() { Width = contentWidth, Spacing = 10 };
        ProgressRing loadingRing = new() { IsActive = true, Width = 36, Height = 36 };
        content.Children.Add(loadingRing);
        content.Children.Add(new TextBlock { Text = UiText.T("ImageComparisonDialog.Text001"), TextWrapping = TextWrapping.Wrap });
        Button close = DialogLayout.Button("✕"); close.HorizontalAlignment = HorizontalAlignment.Right;
        ToolTipService.SetToolTip(close, UiText.T("ImageComparisonDialog.Text002"));
        Grid title = new() { Width = contentWidth };
        title.Children.Add(new TextBlock { Text = UiText.T("ImageComparisonDialog.Text003"), FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(close);
        ContentDialog dialog = new PerceptoXDialog() { XamlRoot = owner.XamlRoot, Title = title, TitleTemplate = null,
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Clamp(owner.XamlRoot.Size.Height - 230, 180, 720),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = UiText.T("ImageComparisonDialog.Text004"), DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = 1120d;
        RadioButton zoomTwo = new() { Content = "x2", GroupName = "ComparisonZoom", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        RadioButton zoomFour = new() { Content = "x4", GroupName = "ComparisonZoom", VerticalAlignment = VerticalAlignment.Center };
        StackPanel zoomOptions = new() { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center };
        zoomOptions.Children.Add(new TextBlock { Text = UiText.T("ImageComparisonDialog.Text005"), VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        zoomOptions.Children.Add(zoomTwo); zoomOptions.Children.Add(zoomFour);
        Border zoomBar = new() { Name = "ComparisonZoomBar", HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(16, 6, 16, 6),
            CornerRadius = new CornerRadius(10), Background = ThemeManager.Brush("PxSubtle"),
            BorderThickness = new Thickness(1), BorderBrush = ThemeManager.Brush("PxLine"), Child = zoomOptions };
        ToggleSwitch heatmap = new() { Header = UiText.T("ImageComparisonDialog.Text006"), OnContent = UiText.T("Common.On"), OffContent = UiText.T("Common.Off") };
        ToggleSwitch aligned = new() { Header = UiText.T("ImageComparisonDialog.Text007"), OnContent = UiText.T("Common.On"), OffContent = UiText.T("Common.Off") };
        Button reset = DialogLayout.Button(UiText.T("ImageComparisonDialog.Text008"));
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        Task? operation = null;
        BitmapImage? original = null, difference = null, alignedBitmap = null;

        void RenderLoupe()
        {
            if (closed || left is null || right is null) return;
            if (position is not { } point) { left.HideLoupe(); right.HideLoupe(); return; }
            double factor = zoomFour.IsChecked == true ? 4 : 2;
            left.ShowLoupe(point, factor); right.ShowLoupe(point, factor);
        }
        void Hover(RelativeImagePoint? point)
        {
            position = point;
            if (point is null) { timer.Stop(); RenderLoupe(); }
            else if (!timer.IsEnabled) timer.Start();
        }
        void Tick(object? _, object __) { timer.Stop(); RenderLoupe(); }
        void ZoomChanged(object sender, RoutedEventArgs args) => RenderLoupe();
        void CandidateChanged(object sender, RoutedEventArgs args)
        {
            if (right is null || preview is null || original is null) return;
            BitmapImage bitmap = heatmap.IsOn ? difference! : aligned.IsOn ? alignedBitmap! : original;
            bool mapped = heatmap.IsOn || aligned.IsOn;
            right.SetSource(bitmap, mapped ? preview.ReferenceWidth : preview.CandidateWidth,
                mapped ? preview.ReferenceHeight : preview.CandidateHeight);
            RenderLoupe();
        }
        void Entered(object sender, PointerRoutedEventArgs args) => active = (ScrollViewer)sender;
        void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
        {
            if (left is null || right is null || active is null || !ReferenceEquals(active, sender)) return;
            Synchronize(active, ReferenceEquals(active, left.Viewer) ? right.Viewer : left.Viewer);
        }
        void Reset(object sender, RoutedEventArgs args)
        {
            if (left is null || right is null) return;
            active = left.Viewer; left.Viewer.ChangeView(0, 0, 1); right.Viewer.ChangeView(0, 0, 1);
            Hover(null);
        }
        void Close(object sender, RoutedEventArgs args) => dialog.Hide();
        void Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            closed = true; cancellation.Cancel(); timer.Stop();
        }
        void Unloaded(object sender, RoutedEventArgs args)
        {
            closed = true; cancellation.Cancel(); timer.Stop(); dialog.Hide();
        }
        async Task PrepareAsync()
        {
            try
            {
                preview = await VisualComparisonService.CreateAsync(reference, candidate, App.ModernDecoder, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                BitmapImage referenceBitmap = await LoadBitmapAsync(preview.ReferencePath, cancellation.Token);
                original = await LoadBitmapAsync(preview.CandidatePath, cancellation.Token);
                if (preview.HasDifference)
                {
                    difference = await LoadBitmapAsync(preview.DifferencePath, cancellation.Token);
                    alignedBitmap = await LoadBitmapAsync(preview.AlignedCandidatePath, cancellation.Token);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                double paneHeight = Math.Clamp(owner.XamlRoot.Size.Height - 560, 180, 340);
                left = new(UiText.T("SearchPage.Text003"), preview.Reference, referenceBitmap, preview.ReferenceWidth, preview.ReferenceHeight, (contentWidth - 12) / 2, paneHeight);
                right = new(UiText.T("ImageComparisonDialog.Text009"), preview.Candidate, original, preview.CandidateWidth, preview.CandidateHeight, (contentWidth - 12) / 2, paneHeight);
                Grid images = new() { ColumnSpacing = 12 };
                images.ColumnDefinitions.Add(new ColumnDefinition()); images.ColumnDefinitions.Add(new ColumnDefinition());
                images.Children.Add(left.View); Grid.SetColumn(right.View, 1); images.Children.Add(right.View);
                active = left.Viewer;
                left.HoverChanged += Hover; right.HoverChanged += Hover;
                foreach (ScrollViewer viewer in new[] { left.Viewer, right.Viewer })
                { viewer.PointerEntered += Entered; viewer.ViewChanged += ViewChanged; }
                heatmap.IsEnabled = aligned.IsEnabled = preview.HasDifference;
                content.Children.Clear();
                content.Children.Add(zoomBar); content.Children.Add(images);
                content.Children.Add(new TextBlock { Text = UiText.T("ImageComparisonDialog.Text010"), TextWrapping = TextWrapping.Wrap });
                content.Children.Add(new TextBlock { Text = UiText.T("ImageComparisonDialog.Text011"), TextWrapping = TextWrapping.Wrap });
                content.Children.Add(DialogLayout.Section(UiText.T("ImageComparisonDialog.Text012"), heatmap, aligned, reset));
                content.Children.Add(DialogLayout.Section(UiText.T("ImageComparisonDialog.Text013"), DialogLayout.Text(DescribePreview(preview))));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (closed) return;
                content.Children.Clear(); content.Children.Add(new TextBlock { Text = UiText.T("ImageComparisonDialog.Text014") + error.Message, TextWrapping = TextWrapping.Wrap });
            }
            finally { loadingRing.IsActive = false; }
        }
        void Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) => operation = PrepareAsync();

        timer.Tick += Tick; zoomTwo.Checked += ZoomChanged; zoomFour.Checked += ZoomChanged;
        heatmap.Toggled += CandidateChanged; aligned.Toggled += CandidateChanged;
        reset.Click += Reset; close.Click += Close;
        dialog.Opened += Opened; dialog.Closing += Closing; owner.Unloaded += Unloaded;
        try { await dialog.ShowAsync(); }
        finally
        {
            closed = true; cancellation.Cancel(); timer.Stop();
            timer.Tick -= Tick; zoomTwo.Checked -= ZoomChanged; zoomFour.Checked -= ZoomChanged;
            heatmap.Toggled -= CandidateChanged; aligned.Toggled -= CandidateChanged;
            reset.Click -= Reset; close.Click -= Close;
            dialog.Opened -= Opened; dialog.Closing -= Closing; owner.Unloaded -= Unloaded;
            try { if (operation is not null) await operation; }
            finally
            {
                foreach (ComparisonImagePane? pane in new[] { left, right })
                {
                    if (pane is null) continue;
                    pane.HoverChanged -= Hover; pane.Viewer.PointerEntered -= Entered; pane.Viewer.ViewChanged -= ViewChanged;
                    pane.Dispose();
                }
                dialog.Content = null; dialog.Title = null; content.Children.Clear();
                preview?.Dispose();
            }
        }
    }

    private static string DescribePreview(ComparisonPreview preview) => preview.ExplanationKind switch
    {
        ComparisonExplanationKind.DifferentFraming => UiText.T("Comparison.DifferentFraming"),
        ComparisonExplanationKind.TooDifferent => UiText.T("Comparison.TooDifferent"),
        _ => UiText.T("Comparison.Heatmap", preview.MeanDifference) + (preview.AlignmentRegion is { } box
            ? UiText.T("Comparison.Aligned", box.X, box.Y, box.Width, box.Height) : UiText.T("Comparison.SameFraming"))
    };

    private static async Task<BitmapImage> LoadBitmapAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Decode from an owned stream, not a URI: no delayed file handles survive preview disposal.
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var randomAccess = stream.AsRandomAccessStream();
        BitmapImage bitmap = new();
        await bitmap.SetSourceAsync(randomAccess);
        token.ThrowIfCancellationRequested();
        return bitmap;
    }

    private static void Synchronize(ScrollViewer source, ScrollViewer target)
    {
        double x = source.ScrollableWidth <= 0 ? 0 : source.HorizontalOffset / source.ScrollableWidth;
        double y = source.ScrollableHeight <= 0 ? 0 : source.VerticalOffset / source.ScrollableHeight;
        if (Math.Abs(source.ZoomFactor - target.ZoomFactor) > .001)
        {
            FrameworkElement image = (FrameworkElement)target.Content;
            target.ChangeView(x * Math.Max(0, image.ActualWidth * source.ZoomFactor - target.ViewportWidth),
                y * Math.Max(0, image.ActualHeight * source.ZoomFactor - target.ViewportHeight), source.ZoomFactor, disableAnimation: true);
            return;
        }
        double horizontal = x * target.ScrollableWidth, vertical = y * target.ScrollableHeight;
        if (Math.Abs(target.HorizontalOffset - horizontal) > .5 || Math.Abs(target.VerticalOffset - vertical) > .5)
            target.ChangeView(horizontal, vertical, null, disableAnimation: true);
    }
}
