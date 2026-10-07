using PerceptoX.Presentation.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PerceptoX.Presentation.ViewModels;
using PerceptoX.WinUI.Pages;
using PerceptoX.Infrastructure.Modules;
using System.Diagnostics.CodeAnalysis;
using PerceptoX.WinUI.Services;

namespace PerceptoX.WinUI;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "WinUI owns the Window lifecycle; Closed cancels/disposes the module CTS and the shell.")]
public sealed partial class MainWindow : Window
{
    private ShellViewModel? _shell;
    private bool _isConfiguring;
    private readonly CancellationTokenSource _moduleLifetime = new();
    private bool _disposed;
    private bool _moduleDialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        RootLayout.Language = UiText.Language;
        ThemeManager.Changed += OnThemeChanged;
        UiText.Changed += OnLanguageChanged;
        ApplyTheme();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragRegion);
        ConfigureCaptionColors();
        InitializeShellAppearance();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "graphics", "PerceptoX.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        Closed += OnClosed;
    }

    private void ConfigureCaptionColors()
    {
        // Caption buttons are native: RootLayout.RequestedTheme does not theme them.
        bool dark = ThemeManager.Current == ElementTheme.Dark;
        var titleBar = AppWindow.TitleBar;
        var ink = dark ? Windows.UI.Color.FromArgb(255, 236, 242, 255) : Windows.UI.Color.FromArgb(255, 17, 27, 61);
        titleBar.ButtonForegroundColor = ink;
        titleBar.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(255, 166, 182, 210) : Windows.UI.Color.FromArgb(255, 100, 119, 162);
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonHoverForegroundColor = ink;
        titleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 44, 64, 93) : Windows.UI.Color.FromArgb(255, 224, 238, 255);
        titleBar.ButtonPressedForegroundColor = ink;
        titleBar.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 59, 84, 119) : Windows.UI.Color.FromArgb(255, 196, 220, 250);
    }

    private void OnThemeChanged(object? sender, EventArgs args) => ApplyTheme();
    private void ApplyTheme()
    {
        RootLayout.RequestedTheme = ThemeManager.Current;
        bool dark = ThemeManager.Current == ElementTheme.Dark;
        LightWordmark.Visibility = dark ? Visibility.Collapsed : Visibility.Visible;
        DarkWordmark.Visibility = dark ? Visibility.Visible : Visibility.Collapsed;
        SidebarRibbons.Opacity = dark ? 0.22 : 0.8;
        ConfigureCaptionColors();
        SynchronizeSidebarPreferences();
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        RootLayout.Language = UiText.Language;
        _shell?.RefreshLanguage();
        RefreshModuleStatus();
        RefreshShellLanguage();
    }

    private Func<string>? _moduleText;
    private Windows.UI.Color _moduleColor;
    private void RefreshModuleStatus()
    {
        if (_moduleText is not null) RenderModuleStatus(_moduleText(), _moduleColor);
    }

    public void Configure(ShellViewModel shell, bool startWithSearch = false, ElementTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        if (theme is ElementTheme requestedTheme)
        {
            ThemeManager.Apply(requestedTheme);
        }

        int initialIndex = startWithSearch ? 1 : 0;
        _isConfiguring = true;
        try
        {
            Navigation.SelectedIndex = initialIndex;
        }
        finally
        {
            _isConfiguring = false;
        }

        NavigateTo(startWithSearch ? "search" : "batch");
        SetModuleStatus(() => App.ModernDecoder is null ? UiText.T("MainWindow.xaml.Text001")
            : UiText.T("MainWindow.xaml.Text002"), App.ModernDecoder is null ? Microsoft.UI.Colors.DarkOrange : Microsoft.UI.Colors.SeaGreen);
        _ = CheckModuleUpdatesAtStartupAsync(_moduleLifetime.Token);
    }

    private async Task CheckModuleUpdatesAtStartupAsync(CancellationToken token)
    {
        try
        {
            ModuleUpdateService updates = new(AppContext.BaseDirectory);
            if (!await updates.ShouldCheckAtStartupAsync(token)) return;
            ModuleInspection installed = await new ModuleInstaller(AppContext.BaseDirectory).InspectAsync(token);
            ModuleUpdateOffer? offer = await updates.CheckAsync(installed.Version, token);
            token.ThrowIfCancellationRequested();
            if (offer is not null && !_disposed) SetModuleStatus(() => UiText.T("MainWindow.xaml.Text003", offer.Version), Microsoft.UI.Colors.DodgerBlue);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (!token.IsCancellationRequested && !_disposed) SetModuleStatus(() => UiText.T("MainWindow.xaml.Text004") + error.Message, Microsoft.UI.Colors.Firebrick); }
    }

    private async void OnModulesClick(object sender, RoutedEventArgs args)
        => await ShowModulesAsync();

    private void SetModuleStatus(Func<string> text, Windows.UI.Color color)
    {
        _moduleText = text; _moduleColor = color;
        RefreshModuleStatus();
    }

    private void RenderModuleStatus(string text, Windows.UI.Color color)
    {
        ModuleSummary.Text = text;
        ModuleStatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
        ToolTipService.SetToolTip(ModuleSummary, text);
        ToolTipService.SetToolTip(ModuleStatusDot, text);
    }

    private async Task ShowModulesAsync()
    {
        if (_shell is null || _disposed || _moduleDialogOpen) return;
        if (_shell.Workspace.IsOperationRunning)
        {
            SetModuleStatus(() => UiText.T("MainWindow.xaml.Text005"), Microsoft.UI.Colors.DodgerBlue);
            return;
        }
        _moduleDialogOpen = true;
        ModulesButton.IsEnabled = false;
        try
        {
            bool changed = await Services.ModuleManagerDialog.ShowAsync(
                ((FrameworkElement)Content).XamlRoot, _shell.Workspace, AppContext.BaseDirectory);
            if (changed && !_disposed) SetModuleStatus(() => UiText.T("MainWindow.xaml.Text006"), Microsoft.UI.Colors.DarkOrange);
        }
        catch (Exception exception) { if (!_disposed) SetModuleStatus(() => UiText.T("MainWindow.xaml.Text007") + exception.Message, Microsoft.UI.Colors.Firebrick); }
        finally { _moduleDialogOpen = false; if (!_disposed) ModulesButton.IsEnabled = true; }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_shell is null || _isConfiguring)
        {
            return;
        }

        string? tag = (Navigation.SelectedItem as ListBoxItem)?.Tag?.ToString();
        NavigateTo(tag);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args)
        => ShowSettings();

    public void ShowSettings()
    {
        if (_shell is not null)
        {
            Navigation.SelectedIndex = -1;
            ContentFrame.Navigate(typeof(SettingsPage), _shell.Settings);
            if (ContentFrame.Content is SettingsPage settings) settings.ConfigureModuleManager(ShowModulesAsync);
        }
    }

#if DEBUG
    internal Task ShowModulesPreviewAsync() => ShowModulesAsync();
    internal void ShowBatchPreview() => ShowPagePreview("batch");
    internal void ShowPagePreview(string page)
    {
        _isConfiguring = true;
        try { Navigation.SelectedIndex = page switch { "batch" => 0, "search" => 1, "groups" => 2, "indexing" => 3, _ => throw new ArgumentException("Unknown preview page.") }; }
        finally { _isConfiguring = false; }
        NavigateTo(page);
    }
    internal object AppearanceDiagnostic() => new
    {
        ActualTheme = RootLayout.ActualTheme.ToString(),
        CaptionForeground = AppWindow.TitleBar.ButtonForegroundColor?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CaptionInactiveForeground = AppWindow.TitleBar.ButtonInactiveForegroundColor?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CaptionHover = AppWindow.TitleBar.ButtonHoverBackgroundColor?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CaptionPressed = AppWindow.TitleBar.ButtonPressedBackgroundColor?.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
#endif

    private void NavigateTo(string? tag)
    {
        if (_shell is null)
        {
            return;
        }

        switch (tag)
        {
            case "batch":
                ContentFrame.Navigate(typeof(BatchMatchPage), _shell.BatchMatch);
                break;
            case "indexing":
                ContentFrame.Navigate(typeof(IndexingPage), _shell.Indexing);
                break;
            case "search":
                ContentFrame.Navigate(typeof(SearchPage), _shell.Search);
                break;
            case "groups":
                ContentFrame.Navigate(typeof(SimilarityGroupsPage), _shell.Groups);
                break;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
        => Dispose();

    private void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeShellAppearance();
        ThemeManager.Changed -= OnThemeChanged;
        UiText.Changed -= OnLanguageChanged;
        _moduleLifetime.Cancel();
        _moduleLifetime.Dispose();
        _shell?.Dispose();
        _shell = null;
    }
}
