using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PerceptoX.Presentation.Localization;
using System.Diagnostics;

namespace PerceptoX.WinUI.Services;

internal static class AboutDialog
{
    internal static async Task ShowAsync(XamlRoot root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PerceptoXDialog dialog = new() { XamlRoot = root, DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = 520d;
        TextBlock heading = DialogLayout.Text(""); heading.FontSize = 21; heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        Image icon = new() { Source = new BitmapImage(new Uri("ms-appx:///graphics/icon.png")), Width = 40, Height = 40 };
        Image lightLogo = new() { Source = new BitmapImage(new Uri("ms-appx:///graphics/logo.png")), Width = 143, Stretch = Stretch.Uniform };
        Grid wordmark = new() { Width = 143, Height = 34 };
        Grid darkLogo = new();
        TextBlock darkText = new() { Text = "Percepto", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, VerticalAlignment = VerticalAlignment.Center };
        Image logoX = new() { Source = new BitmapImage(new Uri("ms-appx:///graphics/logo.png")), Width = 143,
            Stretch = Stretch.Uniform, Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(94, 0, 49, 34) } };
        darkLogo.Children.Add(darkText); darkLogo.Children.Add(logoX); wordmark.Children.Add(lightLogo); wordmark.Children.Add(darkLogo);
        StackPanel brand = new() { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        brand.Children.Add(icon); brand.Children.Add(wordmark);
        TextBlock version = DialogLayout.Text(""); version.HorizontalAlignment = HorizontalAlignment.Center;
        TextBlock author = DialogLayout.Text("");
        HyperlinkButton email = new() { Content = "eduard.condact.dev@gmail.com", NavigateUri = new Uri("mailto:eduard.condact.dev@gmail.com"),
            Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        TextBlock description = DialogLayout.Text("");
        TextBlock license = DialogLayout.Text(""); license.FontSize = 12;
        StackPanel content = new() { Spacing = 16, MaxWidth = 420 };
        foreach (UIElement element in new UIElement[] { heading, brand, version, author, email, description, license }) content.Children.Add(element);
        dialog.Content = content;
        string productVersion = FileVersionInfo.GetVersionInfo(typeof(App).Assembly.Location).ProductVersion?.Split('+')[0] ?? "1.0.0";
        bool closed = false;
        void Refresh(object? sender, EventArgs args)
        {
            dialog.RequestedTheme = ThemeManager.Current; dialog.Language = UiText.Language;
            dialog.Resources["ContentDialogBackground"] = ThemeManager.Brush("PxBackground");
            dialog.Resources["ContentDialogTopOverlay"] = ThemeManager.Brush("PxBackground");
            dialog.Resources["ContentDialogBorderBrush"] = ThemeManager.Brush("PxLine");
            heading.Foreground = author.Foreground = description.Foreground = darkText.Foreground = ThemeManager.Brush("PxInk");
            version.Foreground = license.Foreground = ThemeManager.Brush("PxMuted"); email.Foreground = ThemeManager.Brush("PxAccentText");
            lightLogo.Visibility = ThemeManager.Current == ElementTheme.Light ? Visibility.Visible : Visibility.Collapsed;
            darkLogo.Visibility = ThemeManager.Current == ElementTheme.Dark ? Visibility.Visible : Visibility.Collapsed;
            heading.Text = UiText.T("About.Title"); version.Text = UiText.T("About.Version", productVersion);
            author.Text = UiText.T("About.Author"); description.Text = UiText.T("About.Description"); license.Text = UiText.T("About.License");
            dialog.CloseButtonText = UiText.T("About.Close");
        }
        ThemeManager.Changed += Refresh; UiText.Changed += Refresh;
        using CancellationTokenRegistration registration = token.Register(() => root.Content.DispatcherQueue.TryEnqueue(() => { if (!closed) dialog.Hide(); }));
        try { Refresh(null, EventArgs.Empty); await dialog.ShowAsync(); }
        finally { closed = true; ThemeManager.Changed -= Refresh; UiText.Changed -= Refresh; }
    }
}
