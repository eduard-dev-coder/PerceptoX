using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PerceptoX.Presentation.Localization;
using PerceptoX.Presentation.Services;
using PerceptoX.WinUI.Localization;
using PerceptoX.WinUI.Services;

namespace PerceptoX.WinUI;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _donationTimer = new() { Interval = DonationContent.MessagePause };
    private Storyboard? _donationAnimation;
    private TitlebarMessageBlur? _donationBlur;
    private int _donationMessage;
    private bool _appearanceReady;
    private bool _synchronizingPreferences;
    private bool _windowActive;
    private bool _donationHovered;
    private bool _donationFocused;
    private bool _donationDialogOpen;
    private Func<string>? _preferenceText;
#if DEBUG
    private readonly List<long> _previewAutomaticTicks = [];
    private bool _observeAutomaticTicks;
#endif

    private void InitializeShellAppearance()
    {
        _appearanceReady = true;
        _donationTimer.Tick += OnDonationTimerTick;
        Activated += OnShellActivated;
        SidebarLanguagePicker.ItemsSource = LanguageManager.Languages;
        SynchronizeSidebarPreferences();
        RenderDonationMessages();
    }

    private void SynchronizeSidebarPreferences()
    {
        if (!_appearanceReady) return;
        _synchronizingPreferences = true;
        try
        {
            SidebarLanguagePicker.SelectedItem = LanguageManager.Languages.FirstOrDefault(catalog => catalog.Language == UiText.Language);
            SidebarThemeToggle.IsOn = ThemeManager.Current == ElementTheme.Dark;
            SidebarThemeLabel.Text = UiText.T(ThemeManager.Current == ElementTheme.Dark ? "Theme.DarkShort" : "Theme.LightShort");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SidebarThemeToggle, SidebarThemeLabel.Text);
        }
        finally { _synchronizingPreferences = false; }
    }

    private void RefreshShellLanguage()
    {
        if (!_appearanceReady) return;
        SynchronizeSidebarPreferences();
        PauseDonationRotation();
        RenderDonationMessages();
        RenderPreferenceStatus();
        ResumeDonationRotation();
    }

    private void OnSidebarLanguageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_appearanceReady || _synchronizingPreferences || SidebarLanguagePicker.SelectedItem is not LanguageCatalog catalog || catalog.Language == UiText.Language) return;
        try
        {
            _preferenceText = null;
#if DEBUG
            if (App.IsDesignPreview) LanguageManager.Apply(catalog.Language); else
#endif
            LanguageManager.Save(catalog.Language);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SynchronizeSidebarPreferences();
            _preferenceText = () => UiText.T("Language.SaveFailure", error.Message);
        }
        RenderPreferenceStatus();
    }

    private void OnSidebarThemeChanged(object sender, RoutedEventArgs args)
    {
        if (!_appearanceReady || _synchronizingPreferences) return;
        ElementTheme theme = SidebarThemeToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light;
        if (theme == ThemeManager.Current) return;
        try
        {
            _preferenceText = null;
#if DEBUG
            if (App.IsDesignPreview) ThemeManager.Apply(theme); else
#endif
            ThemeManager.SaveAndApply(theme);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SynchronizeSidebarPreferences();
            _preferenceText = () => UiText.T("Theme.SaveFailure", error.Message);
        }
        RenderPreferenceStatus();
    }

    private void RenderPreferenceStatus()
    {
        SidebarPreferenceStatus.Text = _preferenceText?.Invoke() ?? "";
        SidebarPreferenceStatus.Visibility = _preferenceText is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnDonateClick(object sender, RoutedEventArgs args) => await ShowDonationAsync();
    private async void OnAboutClick(object sender, RoutedEventArgs args) => await ShowInformationAsync(AboutDialog.ShowAsync);

    private async Task ShowDonationAsync()
        => await ShowInformationAsync(DonationDialog.ShowAsync);

    private async Task ShowInformationAsync(Func<XamlRoot, CancellationToken, Task> show)
    {
        if (_disposed || _donationDialogOpen || RootLayout.XamlRoot is null) return;
        _donationDialogOpen = true;
        PauseDonationRotation();
        try { await show(RootLayout.XamlRoot, _moduleLifetime.Token); }
        catch (OperationCanceledException) when (_moduleLifetime.IsCancellationRequested) { }
        catch (InvalidOperationException) { /* Another native ContentDialog already owns this XamlRoot. */ }
        finally { _donationDialogOpen = false; if (!_disposed) ResumeDonationRotation(); }
    }

    private void OnTitleBarLoaded(object sender, RoutedEventArgs args)
    {
        UpdateTitleBarInputRegion();
        try { _donationBlur ??= new TitlebarMessageBlur(DonationMessages, DonationBlurHost); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or NotSupportedException)
        { System.Diagnostics.Debug.WriteLine("Titlebar blur unavailable; using fade: " + error.Message); }
        ResumeDonationRotation();
    }

    private void OnTitleBarSizeChanged(object sender, SizeChangedEventArgs args) => UpdateTitleBarInputRegion();

    private void UpdateTitleBarInputRegion()
    {
        if (_disposed || !ExtendsContentIntoTitleBar || TitleDragRegion.XamlRoot is null) return;
        // Native caption pixels are reserved dynamically, including maximized/resize layouts.
        double scale = TitleDragRegion.XamlRoot.RasterizationScale;
        double inset = Math.Max(140, AppWindow.TitleBar.RightInset / scale);
        if (Math.Abs(TitleDragRegion.Margin.Right - inset) > 0.1)
        {
            TitleDragRegion.Margin = new Thickness(0, 0, inset, 0);
            return; // SizeChanged recomputes the button bounds after the new layout.
        }
        if (TitleDragRegion.ActualWidth < 120)
        {
            TitleDonateButton.Visibility = Visibility.Collapsed;
            InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, []);
            return;
        }
        TitleDonateButton.Visibility = Visibility.Visible;
        TitleDonateButton.Width = Math.Max(0, Math.Min(520, TitleDragRegion.ActualWidth - 48));
        CurrentDonationMessage.MaxWidth = NextDonationMessage.MaxWidth = Math.Max(0, TitleDonateButton.Width - 44);
        Windows.Foundation.Rect bounds = TitleDonateButton.TransformToVisual(RootLayout)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, TitleDonateButton.ActualWidth, TitleDonateButton.ActualHeight));
        var rectangle = new Windows.Graphics.RectInt32((int)Math.Floor(bounds.X * scale), (int)Math.Floor(bounds.Y * scale),
            (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale));
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [rectangle]);
    }

    private void OnDonationViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        DonationViewport.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, 24) };
        _donationBlur?.Resize(args.NewSize.Width);
        UpdateTitleBarInputRegion();
    }

    private void OnShellActivated(object sender, WindowActivatedEventArgs args)
    {
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_windowActive) ResumeDonationRotation(); else PauseDonationRotation();
    }

    private void OnDonationPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    { _donationHovered = true; PauseDonationRotation(); }
    private void OnDonationPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    { _donationHovered = false; ResumeDonationRotation(); }
    private void OnDonationGotFocus(object sender, RoutedEventArgs args)
    { _donationFocused = true; PauseDonationRotation(); }
    private void OnDonationLostFocus(object sender, RoutedEventArgs args)
    { _donationFocused = false; ResumeDonationRotation(); }

    private void RenderDonationMessages()
    {
        CurrentDonationMessage.Text = DonationContent.Message(_donationMessage);
        NextDonationMessage.Text = DonationContent.Message(DonationContent.Next(_donationMessage));
    }

    private void ResumeDonationRotation()
    {
        if (!_appearanceReady || _disposed || !_windowActive || _donationDialogOpen || _donationHovered || _donationFocused || _donationAnimation is not null || _donationTimer.IsEnabled) return;
        _donationTimer.Start();
    }

    private void PauseDonationRotation()
    {
        _donationTimer.Stop();
        if (_donationAnimation is not null)
        {
            _donationAnimation.Completed -= OnDonationAnimationCompleted;
            _donationAnimation.Stop();
            _donationAnimation = null;
        }
        DonationTranslation.Y = 0;
        DonationMessages.Opacity = 1;
        _donationBlur?.Stop();
    }

    private void OnDonationTimerTick(object? sender, object args)
    {
#if DEBUG
        if (_observeAutomaticTicks && _donationTimer.IsEnabled) _previewAutomaticTicks.Add(System.Diagnostics.Stopwatch.GetTimestamp());
#endif
        _donationTimer.Stop();
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        { AdvanceDonationMessage(); return; }
        DoubleAnimation movement = new()
        {
            From = 0, To = -24, Duration = new Duration(TimeSpan.FromMilliseconds(700)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(movement, DonationTranslation);
        Storyboard.SetTargetProperty(movement, "Y");
        _donationAnimation = new Storyboard();
        _donationAnimation.Children.Add(movement);
        DoubleAnimationUsingKeyFrames fade = new() { Duration = movement.Duration };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 1, KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero) });
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 0.3, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)) });
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 1, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)) });
        Storyboard.SetTarget(fade, DonationMessages); Storyboard.SetTargetProperty(fade, "Opacity");
        _donationAnimation.Children.Add(fade);
        _donationBlur?.Begin(TimeSpan.FromMilliseconds(700));
        _donationAnimation.Completed += OnDonationAnimationCompleted;
        _donationAnimation.Begin();
    }

    private void OnDonationAnimationCompleted(object? sender, object args)
    {
        PauseDonationRotation();
        AdvanceDonationMessage();
    }

    private void AdvanceDonationMessage()
    {
        _donationMessage = DonationContent.Next(_donationMessage);
        RenderDonationMessages();
        ResumeDonationRotation();
    }

    private void DisposeShellAppearance()
    {
        PauseDonationRotation();
        _donationTimer.Tick -= OnDonationTimerTick;
        Activated -= OnShellActivated;
        _appearanceReady = false;
        _donationBlur?.Dispose(); _donationBlur = null;
    }

#if DEBUG
    internal Task ShowDonationPreviewAsync() => ShowDonationAsync();
    internal Task ShowAboutPreviewAsync() => ShowInformationAsync(AboutDialog.ShowAsync);
    internal bool MatchesSidebarAppearancePreview() => SidebarThemeToggle.IsOn == (ThemeManager.Current == ElementTheme.Dark)
        && (SidebarLanguagePicker.SelectedItem as LanguageCatalog)?.Language == UiText.Language;
    internal void SelectSidebarLanguagePreview(string code) => SidebarLanguagePicker.SelectedItem = LanguageManager.Languages.First(catalog => catalog.Language == code);
    internal void SelectSidebarThemePreview(ElementTheme theme) => SidebarThemeToggle.IsOn = theme == ElementTheme.Dark;
    internal async Task VerifyDonationCyclePreviewAsync()
    {
        bool wasHovered = _donationHovered;
        _donationHovered = true;
        PauseDonationRotation();
        int first = _donationMessage;
        try
        {
            for (int index = 0; index < DonationContent.MessageCount; index++)
            {
                RootLayout.UpdateLayout();
                await Task.Delay(50);
                int expected = DonationContent.Next(_donationMessage);
                var buttonBefore = TitleDonateButton.TransformToVisual(RootLayout).TransformBounds(new Windows.Foundation.Rect(0, 0, TitleDonateButton.ActualWidth, TitleDonateButton.ActualHeight));
                var incomingBefore = NextDonationMessage.TransformToVisual(RootLayout).TransformBounds(new Windows.Foundation.Rect(0, 0, NextDonationMessage.ActualWidth, NextDonationMessage.ActualHeight));
                OnDonationTimerTick(null, EventArgs.Empty);
                await Task.Delay(950);
                var buttonAfter = TitleDonateButton.TransformToVisual(RootLayout).TransformBounds(new Windows.Foundation.Rect(0, 0, TitleDonateButton.ActualWidth, TitleDonateButton.ActualHeight));
                var incomingAfter = CurrentDonationMessage.TransformToVisual(RootLayout).TransformBounds(new Windows.Foundation.Rect(0, 0, CurrentDonationMessage.ActualWidth, CurrentDonationMessage.ActualHeight));
                if (Math.Abs(buttonBefore.X - buttonAfter.X) > 0.1 || Math.Abs(buttonBefore.Width - buttonAfter.Width) > 0.1 || Math.Abs(incomingBefore.X - incomingAfter.X) > 0.1)
                    throw new InvalidOperationException($"Donation message shifted: button X {buttonBefore.X}->{buttonAfter.X}, width {buttonBefore.Width}->{buttonAfter.Width}, text X {incomingBefore.X}->{incomingAfter.X}.");
                if (_donationMessage != expected || DonationTranslation.Y != 0 || CurrentDonationMessage.Text != DonationContent.Message(expected))
                    throw new InvalidOperationException("Donation transition did not complete or lost localization.");
            }
            if (_donationMessage != first) throw new InvalidOperationException("Donation cycle did not wrap after five messages.");
        }
        finally { _donationHovered = wasHovered; PauseDonationRotation(); ResumeDonationRotation(); }
    }
    internal async Task<double[]> VerifyAutomaticDonationPausePreviewAsync()
    {
        PauseDonationRotation();
        if (!_windowActive || _donationHovered || _donationFocused) throw new InvalidOperationException("Automatic donation preview requires an active, unpaused window.");
        _previewAutomaticTicks.Clear(); _observeAutomaticTicks = true;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            ResumeDonationRotation();
            while (_previewAutomaticTicks.Count < 2 && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds < 18) await Task.Delay(100);
            if (_previewAutomaticTicks.Count < 2) throw new InvalidOperationException("Automatic donation timer did not deliver two ticks.");
            double[] intervals = [System.Diagnostics.Stopwatch.GetElapsedTime(start, _previewAutomaticTicks[0]).TotalSeconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(_previewAutomaticTicks[0], _previewAutomaticTicks[1]).TotalSeconds];
            if (intervals.Any(seconds => seconds < 5.9)) throw new InvalidOperationException("Donation message pause was shorter than six seconds.");
            return intervals;
        }
        finally { _observeAutomaticTicks = false; PauseDonationRotation(); ResumeDonationRotation(); }
    }
    internal object ShellAppearanceDiagnostic() => new
    {
        Navigation = Navigation.Items.Cast<ListBoxItem>().Select(item => item.Tag?.ToString()).ToArray(),
        SidebarLanguage = (SidebarLanguagePicker.SelectedItem as LanguageCatalog)?.Language,
        SidebarDark = SidebarThemeToggle.IsOn, MessageIndex = _donationMessage, Message = CurrentDonationMessage.Text,
        ThemeLabel = SidebarThemeLabel.Text, BlurAvailable = _donationBlur is not null,
        PauseSeconds = _donationTimer.Interval.TotalSeconds, RotationRunning = _donationTimer.IsEnabled,
        TitleButton = TitleDonateButton.TransformToVisual(RootLayout).TransformBounds(new Windows.Foundation.Rect(0, 0, TitleDonateButton.ActualWidth, TitleDonateButton.ActualHeight)),
        CaptionInset = TitleDragRegion.Margin.Right,
        NativeCaptions = "XAML capture does not certify native mouse hit testing or caption rendering."
    };
#endif
}
