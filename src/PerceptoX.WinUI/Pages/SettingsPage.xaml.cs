using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PerceptoX.Presentation.ViewModels;
using PerceptoX.Presentation.Localization;
using PerceptoX.WinUI.Localization;
using PerceptoX.WinUI.Services;
using Microsoft.UI.Xaml;

namespace PerceptoX.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        _configuring = true;
        LightThemeOption.IsChecked = ThemeManager.Current == ElementTheme.Light;
        DarkThemeOption.IsChecked = ThemeManager.Current == ElementTheme.Dark;
        _configuring = false;
        ThemeStatus.Text = string.IsNullOrEmpty(ThemeManager.Diagnostic) ? "" : UiText.T(ThemeManager.Diagnostic);
        LanguagePicker.ItemsSource = LanguageManager.Languages;
        LanguagePicker.SelectedItem = LanguageManager.Languages.FirstOrDefault(catalog => catalog.Language == LanguageManager.PendingLanguage);
        LanguageStatus.Text = string.IsNullOrEmpty(LanguageManager.Diagnostic) ? "" : UiText.T(LanguageManager.Diagnostic);
        Loaded += OnSettingsLoaded;
        Unloaded += OnSettingsUnloaded;
    }
    private Func<Task>? _showModules;
    private bool _configuring;
    private bool _appearanceSubscribed;

    private void OnSettingsLoaded(object sender, RoutedEventArgs args)
    {
        if (!_appearanceSubscribed)
        {
            ThemeManager.Changed += OnExternalPreferenceChanged;
            UiText.Changed += OnExternalPreferenceChanged;
            _appearanceSubscribed = true;
        }
        OnExternalPreferenceChanged(null, EventArgs.Empty);
    }

    private void OnSettingsUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_appearanceSubscribed) return;
        ThemeManager.Changed -= OnExternalPreferenceChanged;
        UiText.Changed -= OnExternalPreferenceChanged;
        _appearanceSubscribed = false;
    }

    private void OnExternalPreferenceChanged(object? sender, EventArgs args)
    {
        _configuring = true;
        try
        {
            LightThemeOption.IsChecked = ThemeManager.Current == ElementTheme.Light;
            DarkThemeOption.IsChecked = ThemeManager.Current == ElementTheme.Dark;
            LanguagePicker.SelectedItem = LanguageManager.Languages.FirstOrDefault(catalog => catalog.Language == UiText.Language);
        }
        finally { _configuring = false; }
    }

    private void OnThemeChanged(object sender, RoutedEventArgs args)
    {
        if (_configuring || sender is not RadioButton option) return;
        ElementTheme theme = option.Tag?.ToString() == "dark" ? ElementTheme.Dark : ElementTheme.Light;
        try
        {
#if DEBUG
            if (App.IsDesignPreview) ThemeManager.Apply(theme); else
#endif
            ThemeManager.SaveAndApply(theme);
            ThemeStatus.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _configuring = true;
            LightThemeOption.IsChecked = ThemeManager.Current == ElementTheme.Light;
            DarkThemeOption.IsChecked = ThemeManager.Current == ElementTheme.Dark;
            _configuring = false;
            ThemeStatus.Text = UiText.T("Theme.SaveFailure", error.Message);
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_configuring || LanguagePicker.SelectedItem is not LanguageCatalog catalog || catalog.Language == LanguageManager.PendingLanguage) return;
        try
        {
#if DEBUG
            if (App.IsDesignPreview) LanguageManager.Apply(catalog.Language); else
#endif
            LanguageManager.Save(catalog.Language);
            LanguageStatus.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _configuring = true;
            LanguagePicker.SelectedItem = LanguageManager.Languages.First(catalog => catalog.Language == LanguageManager.PendingLanguage);
            _configuring = false;
            LanguageStatus.Text = UiText.T("Language.SaveFailure", error.Message);
        }
    }

    internal void ConfigureModuleManager(Func<Task> showModules) => _showModules = showModules;

    private async void OnManageModulesClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (_showModules is null) return;
        ManageModulesButton.IsEnabled = false;
        try { await _showModules(); }
        finally { ManageModulesButton.IsEnabled = true; }
    }

#if DEBUG
    internal void ShowMaintenancePreview() => SettingsTabs.SelectedIndex = 3;
    internal bool MatchesAppearancePreview() => LightThemeOption.IsChecked == (ThemeManager.Current == ElementTheme.Light)
        && DarkThemeOption.IsChecked == (ThemeManager.Current == ElementTheme.Dark)
        && (LanguagePicker.SelectedItem as LanguageCatalog)?.Language == UiText.Language;
    internal void ShowTabPreview(int index) => SettingsTabs.SelectedIndex = index;
    internal void SelectLanguagePreview(string code) => LanguagePicker.SelectedItem = LanguageManager.Languages.First(catalog => catalog.Language == code);
    internal void SelectThemePreview(ElementTheme theme)
    {
        if (theme == ElementTheme.Dark) DarkThemeOption.IsChecked = true; else LightThemeOption.IsChecked = true;
    }
#endif

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DataContext = e.Parameter as SettingsViewModel;
    }
}
