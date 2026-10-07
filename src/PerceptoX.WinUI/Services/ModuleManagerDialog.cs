using PerceptoX.Presentation.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Modules;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.WinUI.Services;

internal static class ModuleManagerDialog
{
    internal static async Task<bool> ShowAsync(XamlRoot root, WorkspaceViewModel workspace, string applicationDirectory)
    {
        ModuleInstaller installer = new(applicationDirectory);
        ModuleUpdateService updates = new(applicationDirectory);
        TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
        TextBlock offerText = new() { TextWrapping = TextWrapping.Wrap };
        Button install = DialogLayout.Button(UiText.T("ModuleManagerDialog.Text001"));
        Button check = DialogLayout.Button(UiText.T("ModuleManagerDialog.Text002")); check.IsEnabled = updates.IsConfigured;
        Button update = DialogLayout.Button(UiText.T("ModuleManagerDialog.Text003")); update.IsEnabled = false;
        Button stop = DialogLayout.Button(UiText.T("ModuleManagerDialog.Text004")); stop.IsEnabled = false;
        CheckBox consent = new() { Content = UiText.T("ModuleManagerDialog.Text005") };
        CheckBox startupCheck = new() { Content = UiText.T("ModuleManagerDialog.Text006"), IsEnabled = updates.IsConfigured };
        ProgressBar progress = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        StackPanel content = new() { Width = Math.Clamp(root.Size.Width - 160, 280, 520), Spacing = 12 };
        content.Children.Add(DialogLayout.Section(UiText.T("SettingsPage.Text048"), DialogLayout.Text(UiText.T("ModuleManagerDialog.Text007", Environment.Version))));
        content.Children.Add(DialogLayout.Section(UiText.T("ModuleManagerDialog.Text008"), status,
            DialogLayout.Text(UiText.T("ModuleManagerDialog.Text009"))));
        Expander onlineOptions = new() { Header = UiText.T("ModuleManagerDialog.Text010"), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = DialogLayout.Section(UiText.T("ModuleManagerDialog.Text010"), DialogLayout.Text(updates.IsConfigured
            ? UiText.T("ModuleManagerDialog.Text011")
            : UiText.T("ModuleManagerDialog.Text012")), offerText, startupCheck, check) };
        content.Children.Add(DialogLayout.Section(UiText.T("ModuleManagerDialog.Text013"), consent,
            DialogLayout.Text(UiText.T("ModuleManagerDialog.Text014")), install, update));
        content.Children.Add(onlineOptions);
        content.Children.Add(progress);
        content.Children.Add(stop);
        ContentDialog dialog = new PerceptoXDialog() { XamlRoot = root, Title = UiText.T("ModuleManagerDialog.Text015"),
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Clamp(root.Size.Height - 200, 200, 650), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = UiText.T("ImageComparisonDialog.Text004"), DefaultButton = ContentDialogButton.Close };
        bool busy = false, changed = false;
        CancellationTokenSource? operation = null;
        ModuleUpdateOffer? offer = null;
        string version = "0.0.0.0";
        try { startupCheck.IsChecked = await updates.ShouldCheckAtStartupAsync(); }
        catch (Exception error) { status.Text = UiText.T("ModuleManagerDialog.Text016") + error.Message; }
        try
        {
            ModuleInspection inspection = await installer.InspectAsync();
            version = inspection.Version;
            status.Text = inspection.ExecutablePath is null ? UiText.T("Module.NeedsInstall") : UiText.T("Module.Installed", version);
            install.IsEnabled = inspection.PayloadAvailable;
        }
        catch (Exception error) { status.Text = UiText.T("ModuleManagerDialog.Text018") + error.Message; }

        void SetBusy(bool value)
        {
            busy = value;
            install.IsEnabled = !value && !changed;
            check.IsEnabled = !value && !changed && updates.IsConfigured;
            update.IsEnabled = !value && !changed && offer is not null;
            consent.IsEnabled = !value;
            startupCheck.IsEnabled = !value && updates.IsConfigured;
            stop.IsEnabled = value;
            dialog.IsEnabled = true;
            progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
        async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (busy || workspace.IsOperationRunning) return;
            operation = new();
            workspace.BeginExternalOperation();
            SetBusy(true);
            try { await action(operation.Token); }
            catch (OperationCanceledException) { status.Text = UiText.T("ModuleManagerDialog.Text019"); }
            catch (Exception error) { status.Text = UiText.T("ModuleManagerDialog.Text020") + error.Message; }
            finally
            {
                workspace.EndExternalOperation();
                SetBusy(false);
                operation.Dispose();
                operation = null;
            }
        }
        IProgress<ModuleInstallProgress> reporter = new Progress<ModuleInstallProgress>(value =>
        {
            if (!busy) return;
            progress.IsIndeterminate = false;
            progress.Value = value.Total == 0 ? 0 : 100d * value.Completed / value.Total;
            status.Text = $"{value.Completed}/{value.Total} · {value.FileName}";
        });
        static async Task VerifyRuntime(string executable, CancellationToken token)
        {
            OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(executable, token);
            if (!decoder.SupportsExtension(".heic") || !decoder.SupportsExtension(".heif") || !decoder.SupportsExtension(".avif"))
                throw new InvalidDataException(UiText.T("ModuleManagerDialog.Text021"));
        }
        install.Click += async (_, _) =>
        {
            if (consent.IsChecked != true) { status.Text = UiText.T("ModuleManagerDialog.Text022"); return; }
            await RunAsync(async token =>
            {
                progress.IsIndeterminate = true;
                await installer.InstallAsync(true, VerifyRuntime, reporter, token: token);
                changed = true;
                status.Text = UiText.T("ModuleManagerDialog.Text023");
            });
        };
        check.Click += async (_, _) => await RunAsync(async token =>
        {
            progress.IsIndeterminate = true;
            offer = await updates.CheckAsync(version, token);
            offerText.Text = offer is null ? UiText.T("ModuleManagerDialog.Text024")
                : UiText.T("ModuleManagerDialog.Text025", offer.Version, offer.Bytes / 1024d / 1024d, new Uri(offer.PackageUrl).Host, offer.LicenseSummary);
            consent.IsChecked = false; // Each offered update needs fresh consent after showing its license notice.
        });
        update.Click += async (_, _) =>
        {
            if (consent.IsChecked != true || offer is null) { status.Text = UiText.T("ModuleManagerDialog.Text026"); return; }
            ModuleUpdateOffer selected = offer;
            await RunAsync(async token =>
            {
                progress.IsIndeterminate = true;
                await updates.ApplyAsync(selected, true, VerifyRuntime, reporter, token);
                changed = true;
                status.Text = UiText.T("ModuleManagerDialog.Text027");
            });
        };
        stop.Click += (_, _) => operation?.Cancel();
        startupCheck.Click += async (_, _) =>
        {
            try { await updates.SetStartupChecksAsync(startupCheck.IsChecked == true); }
            catch (Exception error) { status.Text = UiText.T("ModuleManagerDialog.Text028") + error.Message; }
        };
        dialog.Closing += (_, args) => { if (busy) { args.Cancel = true; status.Text = UiText.T("ModuleManagerDialog.Text029"); } };
        await dialog.ShowAsync();
        return changed;
    }
}
