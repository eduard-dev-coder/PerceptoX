using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PerceptoX.Presentation.Localization;
using PerceptoX.Presentation.Services;
using Windows.System;

namespace PerceptoX.WinUI.Services;

/// <summary>Voluntary donation notice; the fixed PayPal URL opens only after an explicit click.</summary>
internal static class DonationDialog
{
    internal static async Task ShowAsync(XamlRoot root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PerceptoXDialog dialog = new() { XamlRoot = root, DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = 480d;
        FontIcon heart = new() { Glyph = "\uEB52", FontSize = 22 };
        TextBlock heading = new() { FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        StackPanel title = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        title.Children.Add(heart); title.Children.Add(heading);
        TextBlock description = DialogLayout.Text("");
        TextBlock optional = DialogLayout.Text(""); optional.FontSize = 12;
        TextBlock external = DialogLayout.Text(""); external.FontSize = 12;
        TextBlock link = DialogLayout.Text(DonationContent.PayPalUri.AbsoluteUri);
        link.FontSize = 11; link.IsTextSelectionEnabled = true;
        TextBlock failure = DialogLayout.Text(""); failure.Visibility = Visibility.Collapsed; failure.FontSize = 12;
        TextBlock actionText = new();
        FontIcon externalIcon = new() { Glyph = "\uE8A7", FontSize = 16 };
        StackPanel action = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        action.Children.Add(externalIcon); action.Children.Add(actionText);
        Button paypal = new() { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["PxDialogPrimaryButton"],
            Content = action, HorizontalAlignment = HorizontalAlignment.Stretch };
        StackPanel content = new() { Spacing = 16, MaxWidth = 400 };
        content.Children.Add(title); content.Children.Add(description); content.Children.Add(optional); content.Children.Add(paypal);
        content.Children.Add(external); content.Children.Add(link); content.Children.Add(failure);
        dialog.Content = content;
        bool closed = false;
        bool failed = false;

        void Refresh(object? sender, EventArgs args)
        {
            dialog.Language = UiText.Language; dialog.RequestedTheme = ThemeManager.Current;
            dialog.Resources["ContentDialogBackground"] = ThemeManager.Brush("PxBackground");
            dialog.Resources["ContentDialogTopOverlay"] = ThemeManager.Brush("PxBackground");
            dialog.Resources["ContentDialogBorderBrush"] = ThemeManager.Brush("PxLine");
            dialog.Resources["ContentDialogTitleForeground"] = ThemeManager.Brush("PxInk");
            dialog.Resources["ContentDialogContentForeground"] = ThemeManager.Brush("PxInk");
            heart.Foreground = ThemeManager.Brush("PxHeart"); heading.Foreground = description.Foreground = ThemeManager.Brush("PxInk");
            optional.Foreground = external.Foreground = ThemeManager.Brush("PxMuted"); link.Foreground = ThemeManager.Brush("PxAccentText");
            failure.Foreground = ThemeManager.Brush("PxWarningInk");
            heading.Text = UiText.T("Donation.Title"); description.Text = UiText.T("Donation.Description");
            optional.Text = UiText.T("Donation.Optional"); external.Text = UiText.T("Donation.External");
            actionText.Text = UiText.T("Donation.PayPal"); dialog.CloseButtonText = UiText.T("Donation.Close");
            failure.Text = failed ? UiText.T("Donation.OpenFailure") : "";
        }

        async void OpenPayPal(object sender, RoutedEventArgs args)
        {
            paypal.IsEnabled = false;
            bool opened;
            try { opened = await Launcher.LaunchUriAsync(DonationContent.PayPalUri); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
            { opened = false; }
            if (closed) return;
            paypal.IsEnabled = true; failed = !opened;
            failure.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
            Refresh(null, EventArgs.Empty);
        }

        paypal.Click += OpenPayPal;
        ThemeManager.Changed += Refresh; UiText.Changed += Refresh;
        using CancellationTokenRegistration registration = token.Register(() => root.Content.DispatcherQueue.TryEnqueue(() => { if (!closed) dialog.Hide(); }));
        try { Refresh(null, EventArgs.Empty); await dialog.ShowAsync(); }
        finally { closed = true; paypal.Click -= OpenPayPal; ThemeManager.Changed -= Refresh; UiText.Changed -= Refresh; }
    }
}
