using PerceptoX.Presentation.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PerceptoX.Presentation.Services;

namespace PerceptoX.WinUI.Services;

public sealed class WinUiConfirmationService(Func<XamlRoot> root) : IConfirmationService
{
    public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ContentDialog dialog = new PerceptoXDialog()
        {
            XamlRoot = root(), Title = title,
            Content = new ScrollViewer { MaxHeight = 400, Content = DialogLayout.Section(UiText.T("SmartCleanupDialog.Text032"), DialogLayout.Text(message)) },
            PrimaryButtonText = UiText.T("SmartCleanupDialog.Text033"), CloseButtonText = UiText.T("BatchMatchPage.Text012"), DefaultButton = ContentDialogButton.Close
        };
        ContentDialogResult result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary;
    }
}
