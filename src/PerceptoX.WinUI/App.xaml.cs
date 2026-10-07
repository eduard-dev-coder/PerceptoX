using PerceptoX.Presentation.Localization;
using Microsoft.UI.Xaml;
using SixLabors.ImageSharp;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.ViewModels;
using PerceptoX.WinUI.Services;
using WinRT.Interop;
using PerceptoX.Infrastructure.Maintenance;
using PerceptoX.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Calibration;
using PerceptoX.Infrastructure.Modules;

namespace PerceptoX.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    internal static nint WindowHandle { get; private set; }
    internal static OptionalMagickDecoder? ModernDecoder { get; private set; }
    private MainWindow? _window;
    private ILoggerFactory? _logging;
#if DEBUG
    private static readonly System.Text.Json.JsonSerializerOptions AppearanceJson = new() { IncludeFields = true };
    internal static bool IsDesignPreview => Environment.GetCommandLineArgs().Any(argument => argument.StartsWith("--design-preview=", StringComparison.Ordinal));
#endif

    public App()
    {
#if DEBUG
        string? previewLanguage = Environment.GetCommandLineArgs().FirstOrDefault(argument => argument.StartsWith("--design-preview-language=", StringComparison.Ordinal))?.Split('=', 2)[1];
        Localization.LanguageManager.Initialize(previewLanguage);
#else
        Localization.LanguageManager.Initialize();
#endif
        InitializeComponent();
        ThemeManager.Initialize();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Contains("--install-offline-codecs", StringComparer.Ordinal))
        {
            await InstallOfflineCodecsAsync();
            return;
        }
        _window = new MainWindow();
        string logRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PerceptoX", "logs");
        try { _logging = DiagnosticsLog.CreateFactory(logRoot); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { _logging = NullLoggerFactory.Instance; }
        _window.Closed += (_, _) => _logging.Dispose();
        nint windowHandle = WindowNative.GetWindowHandle(_window);
        WindowHandle = windowHandle;
        OptionalMagickDecoder? modernDecoder = null;
        Func<string> codecStatus = () => UiText.T("App.xaml.Text001");
        string? codecPath = null;
        try { codecPath = (await new ModuleInstaller(AppContext.BaseDirectory).InspectAsync()).ExecutablePath; }
        catch (Exception error) { codecStatus = () => UiText.T("App.xaml.Text002") + error.Message; }
#if DEBUG
        // Only developer builds accept external runtimes. Release never depends on PATH or environment configuration.
        codecPath ??= Environment.GetEnvironmentVariable("PERCEPTOX_MAGICK_PATH");
#endif
        if (!string.IsNullOrWhiteSpace(codecPath))
        {
            _window.Activate();
            try
            {
                modernDecoder = await OptionalMagickDecoder.CreateAsync(codecPath);
                codecStatus = () => UiText.T("App.xaml.Text003") + modernDecoder.RuntimeVersion + UiText.T("App.xaml.Text004");
            }
            catch (Exception error) { codecStatus = () => UiText.T("App.xaml.Text005") + error.Message; }
        }
        ModernDecoder = modernDecoder;

#if DEBUG
        bool uxValidation = Environment.GetCommandLineArgs()
            .Skip(1)
            .Any(argument => argument.Equals("--ux-validation", StringComparison.OrdinalIgnoreCase));
        bool designResults = Environment.GetCommandLineArgs().Contains("--design-preview-results", StringComparer.Ordinal);
        IPerceptoXWorkflow workflow = uxValidation || designResults
            ? new UxValidationWorkflow()
            : new DesktopPerceptoXWorkflow(_logging, modernDecoder);
#else
        bool uxValidation = false;
        IPerceptoXWorkflow workflow = new DesktopPerceptoXWorkflow(_logging, modernDecoder);
#endif

        ShellViewModel? shell = null;
        ISimilarityGroupingWorkflow grouping = new DesktopGroupingWorkflow(workflow, modernDecoder);
#if DEBUG
        if (IsDesignPreview && !Environment.GetCommandLineArgs().Any(argument => argument.StartsWith("--documentation-demo-root=", StringComparison.Ordinal)))
            grouping = new DesignGroupingWorkflow();
#endif
        using ImageSharpImageProcessor calibrationProcessor = new(modernDecoder: modernDecoder);
        shell = new ShellViewModel(
            workflow,
            new WinUiPathPickerService(windowHandle, modernDecoder is not null), new DesktopBatchResultActions(() => shell!.Workspace.CreateConfiguration()),
            new MaintenanceService(_logging.CreateLogger<MaintenanceService>()), new WinUiConfirmationService(() => ((FrameworkElement)_window.Content).XamlRoot),
            new JsonCalibrationStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PerceptoX", "calibration-" + calibrationProcessor.ProcessingProfileId + ".json")),
            calibrationProcessor.ProcessingProfileId, grouping);
        shell.Settings.SetCodecStatus(codecStatus);

        ElementTheme? theme = GetRequestedTheme();
        if (uxValidation)
        {
            string validationRoot = Path.Combine(Path.GetTempPath(), "PerceptoX", "ux-validation-v1");
            shell.Workspace.LibraryRoot = validationRoot;
            shell.Workspace.DatabasePath = Path.Combine(validationRoot, "ux-validation.db");
            shell.Workspace.ThumbnailCacheRoot = Path.Combine(validationRoot, "thumbs");
            shell.Settings.TopN = 1000;
            shell.Search.SelectedImagePath = Path.Combine(validationRoot, "query.jpg");
        }

        _window.Configure(shell, uxValidation, theme);
        _window.Activate();

        if (uxValidation)
        {
            _ = shell.Search.SearchCommand.ExecuteAsync(null);
        }
#if DEBUG
        string? previewArgument = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--design-preview=", StringComparison.Ordinal));
        if (previewArgument is not null)
            _ = CaptureDesignAsync(_window, shell, previewArgument["--design-preview=".Length..], designResults,
                Environment.GetCommandLineArgs().Contains("--design-preview-maintenance", StringComparer.Ordinal));
#endif
    }

    private async Task InstallOfflineCodecsAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--accept-codec-licenses", StringComparer.Ordinal)) { Environment.Exit(2); return; }
        // The installer calls this mode only after explicit license/component consent.
        // A non-activated window keeps WinUI's dispatcher alive during asynchronous validation.
        _window = new MainWindow();
        int result = 0;
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
            await new ModuleInstaller(AppContext.BaseDirectory).InstallAsync(true, async (path, token) =>
            {
                OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(path, token);
                if (!decoder.SupportsExtension(".heic") || !decoder.SupportsExtension(".heif") || !decoder.SupportsExtension(".avif"))
                    throw new InvalidDataException("Offline decoder is missing required formats.");
            }, token: timeout.Token);
        }
        catch (Exception error)
        {
            result = 1;
            string directory = Path.Combine(AppContext.BaseDirectory, "modules");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "offline-install-error.txt"), error.ToString());
        }
        finally { _window.Close(); }
        Environment.Exit(result);
    }

#if DEBUG
    private static async Task CaptureDesignAsync(MainWindow window, ShellViewModel shell, string destination, bool results, bool maintenance)
    {
        try
        {
            // Activate returns before the XAML island is loaded; dialog owners need a live XamlRoot.
            await Task.Delay(800);
            string? documentationRoot = Environment.GetCommandLineArgs()
                .FirstOrDefault(argument => argument.StartsWith("--documentation-demo-root=", StringComparison.Ordinal))?.Split('=', 2)[1];
            if (documentationRoot is not null)
            {
                await DocumentationCapture.SaveAsync(window, shell, documentationRoot, destination);
                return;
            }
            string? tabArgument = Environment.GetCommandLineArgs().FirstOrDefault(argument => argument.StartsWith("--design-preview-tab=", StringComparison.Ordinal));
            if (tabArgument is not null && int.TryParse(tabArgument["--design-preview-tab=".Length..], out int tab))
            {
                window.ShowSettings();
                await Task.Delay(800);
                FindSettingsPage((FrameworkElement)window.Content)?.ShowTabPreview(Math.Clamp(tab, 0, 4));
            }
            if (maintenance)
            {
                window.ShowSettings();
                await Task.Delay(800);
                FindSettingsPage((FrameworkElement)window.Content)?.ShowMaintenancePreview();
                await Task.Delay(200);
            }
            if (results)
            {
                shell.Workspace.LibraryRoot = Path.Combine(destination, "library");
                shell.Workspace.DatabasePath = Path.Combine(destination, "preview.db");
                shell.Workspace.ThumbnailCacheRoot = Path.Combine(destination, "thumbs");
                shell.BatchMatch.QueryRoot = Path.Combine(destination, "references");
                await shell.BatchMatch.MatchCommand.ExecuteAsync(null);
                shell.BatchMatch.SelectBestCommand.Execute(null);
            }
            if (Environment.GetCommandLineArgs().Contains("--design-preview-live", StringComparer.Ordinal))
            {
                await CaptureLivePreferencesAsync(window, shell, destination);
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--design-preview-sidebar", StringComparer.Ordinal))
            {
                await CaptureSidebarAppearanceAsync(window, shell, destination);
                return;
            }
            string? dialogArgument = Environment.GetCommandLineArgs().FirstOrDefault(argument => argument.StartsWith("--design-preview-dialog=", StringComparison.Ordinal));
            string? pageArgument = Environment.GetCommandLineArgs().FirstOrDefault(argument => argument.StartsWith("--design-preview-page=", StringComparison.Ordinal));
            if (pageArgument is not null)
            {
                window.ShowPagePreview(pageArgument.Split('=', 2)[1]);
                if (pageArgument == "--design-preview-page=groups")
                {
                    shell.Groups.FolderText = "D:\\Photos"; shell.Groups.AddFolderCommand.Execute(null);
                    await shell.Groups.AnalyzeCommand.ExecuteAsync(null);
                    if (Environment.GetCommandLineArgs().Contains("--design-preview-live-groups", StringComparer.Ordinal))
                    {
                        var initialRoot = window.Content;
                        var initialRow = shell.Groups.Groups[0];
                        shell.Groups.Members[2].IsSelected = true;
                        int selected = shell.Groups.SelectedCount;
                        foreach (var state in new[] { (ElementTheme.Light, "ro"), (ElementTheme.Dark, "en"), (ElementTheme.Dark, "ro") })
                        {
                            ThemeManager.Apply(state.Item1); Localization.LanguageManager.Apply(state.Item2);
                            if (!ReferenceEquals(initialRoot, window.Content) || !ReferenceEquals(initialRow, shell.Groups.Groups[0]) || selected != shell.Groups.SelectedCount)
                                throw new InvalidOperationException("Live group preferences replaced state.");
                            await DesignPreviewCapture.SaveAsync((FrameworkElement)window.Content, Path.Combine(destination, state.Item1 + "-" + state.Item2));
                        }
                        for (int page = 0; page < 19; page++) await shell.Groups.NextGroupsCommand.ExecuteAsync(null);
                        if (shell.Groups.GroupOffset != 950 || shell.Groups.Groups.Count != 50 || shell.Groups.SelectedCount != selected)
                            throw new InvalidOperationException("Group pagination lost selection.");
                        shell.Groups.InvertSelectionCommand.Execute(null);
                        if (shell.Groups.SelectedCount != 3000 - selected) throw new InvalidOperationException("Inverse selection lost off-page members.");
                        await DesignPreviewCapture.SaveAsync((FrameworkElement)window.Content, Path.Combine(destination, "last-page-inverse"));
                        return;
                    }
                }
            }
            FrameworkElement root = (FrameworkElement)window.Content;
            if (dialogArgument == "--design-preview-dialog=modules")
            {
                await DesignPreviewCapture.SaveDialogAsync(root, destination, window.ShowModulesPreviewAsync);
                return;
            }
            if (dialogArgument == "--design-preview-dialog=donation")
            {
                await DesignPreviewCapture.SaveDialogAsync(root, destination, window.ShowDonationPreviewAsync);
                return;
            }
            if (dialogArgument == "--design-preview-dialog=about")
            {
                await DesignPreviewCapture.SaveDialogAsync(root, destination, window.ShowAboutPreviewAsync);
                return;
            }
            if (dialogArgument == "--design-preview-dialog=scan" && results)
            {
                await DesignPreviewCapture.SaveDialogAsync(root, destination, () => ScanProgressDialog.ShowAsync(shell.BatchMatch, root));
                return;
            }
            if (dialogArgument == "--design-preview-dialog=comparison")
            {
                Directory.CreateDirectory(destination);
                string reference = Path.Combine(destination, "reference.png"), candidate = Path.Combine(destination, "candidate.png");
                using (SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image = new(320, 240, new(44, 131, 224)))
                {
                    image.Save(reference);
                    for (int y = 40; y < 80; y++)
                        for (int x = 50; x < 90; x++) image[x, y] = new(230, 100, 80);
                    image.Save(candidate);
                }
                await DesignPreviewCapture.SaveDialogAsync(root, destination, () => ImageComparisonDialog.ShowAsync(reference, candidate, root));
                return;
            }
            await DesignPreviewCapture.SaveAsync((FrameworkElement)window.Content, destination);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination, "capture-error.txt"), exception.ToString());
        }
        finally { window.Close(); }
    }

    private static Pages.SettingsPage? FindSettingsPage(DependencyObject root)
    {
        if (root is Pages.SettingsPage page) return page;
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
            if (FindSettingsPage(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }

    private static async Task CaptureSidebarAppearanceAsync(MainWindow window, ShellViewModel shell, string destination)
    {
        FrameworkElement root = (FrameworkElement)window.Content;
        int count = shell.BatchMatch.Results.Count, selected = shell.BatchMatch.SelectedCount;
        var first = shell.BatchMatch.Results.FirstOrDefault();
        window.ShowSettings();
        await Task.Delay(400);
        Pages.SettingsPage page = FindSettingsPage(root) ?? throw new InvalidOperationException("Settings unavailable.");
        List<object> states = [];
        foreach (var state in new[] { (ElementTheme.Dark, "en"), (ElementTheme.Light, "ro"), (ElementTheme.Dark, "ro") })
        {
            window.SelectSidebarThemePreview(state.Item1); window.SelectSidebarLanguagePreview(state.Item2);
            await Task.Delay(200);
            if (!page.MatchesAppearancePreview() || !ReferenceEquals(page, FindSettingsPage(root)) || !ReferenceEquals(root, window.Content)
                || root.RequestedTheme != state.Item1 || UiText.Language != state.Item2
                || count != shell.BatchMatch.Results.Count || selected != shell.BatchMatch.SelectedCount || !ReferenceEquals(first, shell.BatchMatch.Results.FirstOrDefault()))
                throw new InvalidOperationException("Sidebar preferences lost state or did not synchronize with Settings.");
            await DesignPreviewCapture.SaveAsync(root, Path.Combine(destination, state.Item1 + "-" + state.Item2));
            states.Add(window.ShellAppearanceDiagnostic());
        }
        // The existing Settings controls must also reflect back into the quick controls.
        page.SelectThemePreview(ElementTheme.Light); page.SelectLanguagePreview("en");
        if (!window.MatchesSidebarAppearancePreview()) throw new InvalidOperationException("Settings preferences did not synchronize back to sidebar.");
        states.Add(window.ShellAppearanceDiagnostic());
        await window.VerifyDonationCyclePreviewAsync();
        double[] automaticIntervals = await window.VerifyAutomaticDonationPausePreviewAsync();
        foreach (var state in new[] { (ElementTheme.Light, "ro"), (ElementTheme.Dark, "en") })
        {
            window.SelectSidebarThemePreview(state.Item1); window.SelectSidebarLanguagePreview(state.Item2);
            await DesignPreviewCapture.SaveDialogAsync(root, Path.Combine(destination, "donation-" + state.Item1 + "-" + state.Item2), window.ShowDonationPreviewAsync);
            await DesignPreviewCapture.SaveDialogAsync(root, Path.Combine(destination, "about-" + state.Item1 + "-" + state.Item2), window.ShowAboutPreviewAsync);
        }
        window.SelectSidebarThemePreview(ElementTheme.Light); window.SelectSidebarLanguagePreview("ro");
        await DesignPreviewCapture.SaveDialogAsync(root, Path.Combine(destination, "donation-live-Dark-en"), window.ShowDonationPreviewAsync, () =>
        {
            window.SelectSidebarThemePreview(ElementTheme.Dark); window.SelectSidebarLanguagePreview("en");
            if (PerceptoXDialog.OpenPreview is not { RequestedTheme: ElementTheme.Dark, Language: "en", CloseButtonText: "Close" })
                throw new InvalidOperationException("Open donation dialog did not switch theme and language live.");
            return Task.CompletedTask;
        });
        foreach (string tag in new[] { "batch", "search", "groups", "indexing" }) window.ShowPagePreview(tag);
        window.ShowBatchPreview();
        await DesignPreviewCapture.SaveAsync(root, Path.Combine(destination, "final-Dark-en"));
        states.Add(window.ShellAppearanceDiagnostic());
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "sidebar-verification.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            States = states, PreservedResults = count, PreservedSelection = selected, SameSettingsPage = true,
            Cycle = "Five animation completions and wrap verified using explicit tick invocation, not a wall-clock/native interaction certification.",
            AutomaticTickIntervalsSeconds = automaticIntervals,
            BrowserOpened = false
        }, AppearanceJson));
    }

    private static async Task CaptureLivePreferencesAsync(MainWindow window, ShellViewModel shell, string destination)
    {
        FrameworkElement root = (FrameworkElement)window.Content;
        int count = shell.BatchMatch.Results.Count, selected = shell.BatchMatch.SelectedCount;
        var first = shell.BatchMatch.Results.FirstOrDefault();
        string query = shell.BatchMatch.QueryRoot, library = shell.Workspace.LibraryRoot;
        window.ShowSettings();
        await Task.Delay(400);
        Pages.SettingsPage page = FindSettingsPage(root) ?? throw new InvalidOperationException("Settings unavailable.");
        List<object> states = [];
        foreach (var state in new[] { (ElementTheme.Dark, "en"), (ElementTheme.Light, "ro"), (ElementTheme.Dark, "ro") })
        {
            page.SelectThemePreview(state.Item1); page.SelectLanguagePreview(state.Item2);
            await Task.Delay(400);
            if (ThemeManager.Current != state.Item1 || UiText.Language != state.Item2 || root.RequestedTheme != state.Item1)
                throw new InvalidOperationException("Preference did not switch immediately.");
            if (!ReferenceEquals(page, FindSettingsPage(root)) || !ReferenceEquals(root, window.Content) ||
                shell.BatchMatch.Results.Count != count || shell.BatchMatch.SelectedCount != selected ||
                !ReferenceEquals(first, shell.BatchMatch.Results.FirstOrDefault()) || query != shell.BatchMatch.QueryRoot || library != shell.Workspace.LibraryRoot)
                throw new InvalidOperationException("Live switch lost UI/workspace state.");
            string target = Path.Combine(destination, state.Item1 + "-" + state.Item2);
            await DesignPreviewCapture.SaveAsync(root, target);
            states.Add(new { Theme = state.Item1.ToString(), Language = state.Item2, SameRoot = true, SameSettingsPage = true, Results = count, Selected = selected, Appearance = window.AppearanceDiagnostic() });
        }
        await File.WriteAllTextAsync(Path.Combine(destination, "live-switch.json"), System.Text.Json.JsonSerializer.Serialize(states));
        window.ShowBatchPreview();
        await DesignPreviewCapture.SaveAsync(root, Path.Combine(destination, "Dark-ro-results"));
    }
#endif

    private static ElementTheme? GetRequestedTheme()
    {
        foreach (string argument in Environment.GetCommandLineArgs().Skip(1))
        {
            if (argument.Equals("--theme=light", StringComparison.OrdinalIgnoreCase))
            {
                return ElementTheme.Light;
            }

            if (argument.Equals("--theme=dark", StringComparison.OrdinalIgnoreCase))
            {
                return ElementTheme.Dark;
            }
        }

        return null;
    }
}
