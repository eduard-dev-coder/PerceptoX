using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XamlApplication = Microsoft.UI.Xaml.Application;
using PerceptoX.Presentation.Localization;

namespace PerceptoX.WinUI.Services;

/// <summary>One visual shell for all app dialogs; consent/cancellation behavior stays with each caller.</summary>
internal sealed class PerceptoXDialog : ContentDialog
{
    private FrameworkElement? _cardLayout;
#if DEBUG
    internal static PerceptoXDialog? OpenPreview { get; private set; }
    internal string ActionLayoutDiagnostic { get; private set; } = "not called";
#endif
    internal PerceptoXDialog()
    {
        Style = (Style)XamlApplication.Current.Resources["PxDialog"];
        Language = UiText.Language;
        RequestedTheme = ThemeManager.Current;
        Resources["ContentDialogMaxWidth"] = 640d;
        Resources["ContentDialogBackground"] = ThemeManager.Brush("PxBackground");
        Resources["ContentDialogTopOverlay"] = ThemeManager.Brush("PxBackground");
        Resources["ContentDialogBorderBrush"] = ThemeManager.Brush("PxLine");
        Resources["ContentDialogTitleForeground"] = ThemeManager.Brush("PxInk");
        Resources["ContentDialogContentForeground"] = ThemeManager.Brush("PxInk");
        Opened += (_, _) =>
        {
            // Opened can precede the popup template's final materialization, particularly with a UIElement title.
            LayoutUpdated += OnDialogLayoutUpdated;
            OnDialogLayoutUpdated(this, EventArgs.Empty);
        };
        Closed += (_, _) =>
        {
            LayoutUpdated -= OnDialogLayoutUpdated;
            if (_cardLayout is not null) _cardLayout.SizeChanged -= OnCardSizeChanged;
            _cardLayout = null;
        };
#if DEBUG
        Opened += (_, _) => OpenPreview = this;
        Closed += (_, _) => { if (ReferenceEquals(OpenPreview, this)) OpenPreview = null; };
#endif
    }

    private void OnDialogLayoutUpdated(object? sender, object args)
    {
        if (!CenterSingleAction()) return;
        LayoutUpdated -= OnDialogLayoutUpdated;
        _cardLayout = FindNamed(this, "BackgroundElement");
        if (_cardLayout is not null) _cardLayout.SizeChanged += OnCardSizeChanged;
    }

    private void OnCardSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Async comparison content changes native sizing after Opened. Reapply after that layout pass.
        _ = DispatcherQueue.TryEnqueue(() => { if (_cardLayout is not null) CenterSingleAction(); });
    }

    private bool CenterSingleAction()
    {
        // Keep native command routing, focus and cancellation. Only lay out a single action differently.
        int actionCount = new[] { PrimaryButtonText, SecondaryButtonText, CloseButtonText }.Count(text => !string.IsNullOrEmpty(text));
        Grid? commands = FindNamed(this, "CommandSpace") as Grid;
#if DEBUG
        ActionLayoutDiagnostic = $"actions={actionCount}; columns={commands?.ColumnDefinitions.Count}; width={commands?.Width}; primary=[{PrimaryButtonText}]; secondary=[{SecondaryButtonText}]; close=[{CloseButtonText}]";
#endif
        if (actionCount != 1) return true;
        if (commands is null || commands.ColumnDefinitions.Count == 0) return false;
        commands.HorizontalAlignment = HorizontalAlignment.Center;
        commands.Width = 180 + commands.Padding.Left + commands.Padding.Right;
        // WinUI's classic and Fluent templates have different column counts; the single action is in the last column.
        int last = commands.ColumnDefinitions.Count - 1;
        for (int index = 0; index < last; index++) commands.ColumnDefinitions[index].Width = new GridLength(0);
        commands.ColumnDefinitions[last].Width = new GridLength(1, GridUnitType.Star);
        return true;
    }

    private static FrameworkElement? FindNamed(DependencyObject root, string name)
    {
        if (root is FrameworkElement element && element.Name == name) return element;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindNamed(VisualTreeHelper.GetChild(root, index), name) is { } match) return match;
        return null;
    }
}

internal static class DialogLayout
{
    internal static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = ThemeManager.Brush("PxInk") };

    internal static Border Section(string heading, params UIElement[] elements)
    {
        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = heading, FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ThemeManager.Brush("PxAccentText") });
        foreach (UIElement element in elements) content.Children.Add(element);
        return new Border { Style = (Style)XamlApplication.Current.Resources["PxCardPanel"], Padding = new Thickness(16), Child = content };
    }

    internal static Button Button(string label) => new() { Content = label, Style = (Style)XamlApplication.Current.Resources["PxButton"] };
}
