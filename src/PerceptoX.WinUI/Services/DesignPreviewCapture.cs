#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Text.Json;
using Windows.Graphics.Imaging;

namespace PerceptoX.WinUI.Services;

/// <summary>Opt-in capture of our own XAML surface; never runs during normal use.</summary>
internal static class DesignPreviewCapture
{
    private static readonly string[] ActionNames = ["PrimaryButton", "SecondaryButton", "CloseButton"];
    private static readonly JsonSerializerOptions LayoutJson = new() { IncludeFields = true };
    private static readonly string[] SettingsNames = ["GeneralSettingsCards", "SearchSettingsCards", "CalibrationSettingsCards", "MaintenanceSettingsCards", "ModuleSettingsCards"];
    internal static async Task SaveDialogAsync(FrameworkElement owner, string destination, Func<Task> showDialog, Func<Task>? onOpened = null)
    {
        Task shown = showDialog();
        ContentDialog? dialog = null;
        try
        {
            for (int attempt = 0; attempt < 100 && dialog is null; attempt++)
            {
                await Task.Delay(50);
                dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(owner.XamlRoot)
                    .Select(popup => FindDialog(popup.Child)).FirstOrDefault(found => found is not null);
                dialog ??= PerceptoXDialog.OpenPreview;
            }
            if (dialog is null)
            {
                await SaveAsync(owner, destination);
                throw new InvalidOperationException("Dialog preview did not open; owner surface saved for diagnosis.");
            }
            if (onOpened is not null) await onOpened();
            await SaveAsync(dialog, destination);
            string[] elements = NamedElements(dialog).ToArray(); // Materialize on the UI thread before asynchronous file I/O.
            await File.WriteAllLinesAsync(Path.Combine(destination, "dialog-elements.txt"), elements);
            if (FindNamed(dialog, "BackgroundElement") is { } card)
            {
                var windowBounds = Bounds(owner); var cardBounds = Bounds(card);
                FrameworkElement? action = ActionNames
                    .Select(name => FindNamed(dialog, name)).FirstOrDefault(element => element is { Visibility: Visibility.Visible, ActualWidth: > 0 });
                var actionBounds = action is null ? default : Bounds(action);
                await File.WriteAllTextAsync(Path.Combine(destination, "dialog-layout.json"), JsonSerializer.Serialize(new
                {
                    Window = windowBounds, Card = cardBounds,
                    ActionLayoutDiagnostic = PerceptoXDialog.OpenPreview?.ActionLayoutDiagnostic,
                    HorizontalCenterError = Math.Abs(cardBounds.X + cardBounds.Width / 2 - windowBounds.X - windowBounds.Width / 2),
                    VerticalCenterError = Math.Abs(cardBounds.Y + cardBounds.Height / 2 - windowBounds.Y - windowBounds.Height / 2),
                    Action = actionBounds, ActionCenterError = action is null ? (double?)null :
                        Math.Abs(actionBounds.X + actionBounds.Width / 2 - cardBounds.X - cardBounds.Width / 2),
                    ZoomBar = FindNamed(dialog, "ComparisonZoomBar") is { } bar ? Bounds(bar) : (Windows.Foundation.Rect?)null
                }, LayoutJson));
            }
        }
        finally { dialog?.Hide(); await shown; }
    }

    private static ContentDialog? FindDialog(DependencyObject root)
    {
        if (root is ContentDialog dialog) return dialog;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindDialog(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    public static async Task SaveAsync(FrameworkElement root, string destination)
    {
        Directory.CreateDirectory(destination);
        await Task.Delay(1200);
        root.UpdateLayout();
        if (FindGroupsPage(root) is { DataContext: PerceptoX.Presentation.ViewModels.SimilarityGroupsViewModel groups })
        {
            await File.WriteAllTextAsync(Path.Combine(destination, "groups.json"), JsonSerializer.Serialize(new
            {
                Language = PerceptoX.Presentation.Localization.UiText.Language, Theme = root.ActualTheme.ToString(),
                Groups = groups.LastRun?.Groups, LoadedGroups = groups.Groups.Count, LoadedMembers = groups.Members.Count,
                groups.SelectedCount, CatalogCompare = PerceptoX.Presentation.Localization.UiText.T("BatchMatchPage.Text039"),
                Compare = groups.Members.FirstOrDefault()?.CompareLabel,
                Dataset = "Synthetic display-only fixtures; not matching-quality or native caption rendering evidence"
            }, LayoutJson));
        }
        if (SettingsNames
            .Select(name => FindNamed(root, name)).FirstOrDefault(element => element is { ActualWidth: > 0 }) is { } settingsCards &&
            VisualTreeHelper.GetParent(settingsCards) is FrameworkElement settingsHost)
        {
            var cardsBounds = Bounds(settingsCards); var hostBounds = Bounds(settingsHost);
            await File.WriteAllTextAsync(Path.Combine(destination, "settings-layout.json"), JsonSerializer.Serialize(new
            {
                Cards = cardsBounds, Host = hostBounds,
                HorizontalCenterError = Math.Abs(cardsBounds.X + cardsBounds.Width / 2 - hostBounds.X - hostBounds.Width / 2)
            }, LayoutJson));
        }
        ListView? list = FindResults(root);
        List<double> scrollFrames = [];
        int maximumRealized = 0;
        if (list is { Items.Count: >= 1000 })
        {
            foreach (int target in new[] { 0, 200, 500, 999, 0 })
            {
                Stopwatch watch = Stopwatch.StartNew();
                list.ScrollIntoView(list.Items[target], ScrollIntoViewAlignment.Leading);
                await NextFrameAsync();
                watch.Stop();
                scrollFrames.Add(watch.Elapsed.TotalMilliseconds);
                maximumRealized = Math.Max(maximumRealized, list.ItemsPanelRoot?.Children.Count ?? 0);
                await Task.Delay(100);
            }
            await File.WriteAllTextAsync(Path.Combine(destination, "scroll.json"), JsonSerializer.Serialize(new
            {
                Results = list.Items.Count, MaximumRealizedContainers = maximumRealized,
                ScrollFrameMilliseconds = scrollFrames,
                VirtualizationVerified = maximumRealized is > 0 and < 200,
                Dataset = "Synthetic UX fixtures only; not matching-quality evidence"
            }));
        }
        RenderTargetBitmap bitmap = new();
        await bitmap.RenderAsync(root);
        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
        using FileStream output = new(Path.Combine(destination, "preview.png"), FileMode.Create);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        await File.WriteAllTextAsync(Path.Combine(destination, "capture.txt"),
            $"Captured XAML: {bitmap.PixelWidth}x{bitmap.PixelHeight}; theme={root.ActualTheme}; scale={root.XamlRoot.RasterizationScale}");
    }

    private static Pages.SimilarityGroupsPage? FindGroupsPage(DependencyObject root)
    {
        if (root is Pages.SimilarityGroupsPage page) return page;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindGroupsPage(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private static ListView? FindResults(DependencyObject root)
    {
        if (root is ListView { Name: "ResultsList" } list) return list;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindResults(VisualTreeHelper.GetChild(root, i)) is ListView found) return found;
        return null;
    }

    private static FrameworkElement? FindNamed(DependencyObject root, string name)
    {
        if (root is FrameworkElement element && element.Name == name) return element;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindNamed(VisualTreeHelper.GetChild(root, index), name) is { } match) return match;
        return null;
    }

    private static IEnumerable<string> NamedElements(DependencyObject root)
    {
        if (root is FrameworkElement { Name.Length: > 0 } element)
            yield return $"{element.Name} ({element.GetType().Name}) {element.ActualWidth:F1}x{element.ActualHeight:F1}";
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (string name in NamedElements(VisualTreeHelper.GetChild(root, index))) yield return name;
    }

    private static Windows.Foundation.Rect Bounds(FrameworkElement element)
    {
        var origin = element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point());
        return new(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static async Task NextFrameAsync()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Rendered(object? sender, object args) => completion.TrySetResult();
        CompositionTarget.Rendering += Rendered;
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { CompositionTarget.Rendering -= Rendered; }
    }
}
#endif
