using PerceptoX.Presentation.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PerceptoX.Application.Cleanup;
using PerceptoX.Infrastructure.Cleanup;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.WinUI.Services;

internal static class SmartCleanupDialog
{
    internal static async Task ShowAsync(BatchMatchViewModel model, XamlRoot root, bool recover)
    {
        model.Workspace.BeginExternalOperation();
        try
        {
            string? archive = await new WinUiPathPickerService(App.WindowHandle).PickFolderAsync(CancellationToken.None);
            if (archive is null) return;
            var configuration = model.Workspace.CreateConfiguration();
            RecoverableSmartCleanupService service = new(configuration.DatabasePath, configuration.ThumbnailCacheRoot,
                [configuration.LibraryRoot], archive);
            CleanupResult result;
            if (recover)
            {
                var operations = service.ListRecoveryOperations();
                if (operations.Count == 0) throw new InvalidOperationException(UiText.T("SmartCleanupDialog.Text001"));
                ComboBox selector = new() { Header = UiText.T("SmartCleanupDialog.Text002"), ItemsSource = operations, SelectedIndex = 0, MinWidth = 400 };
                if (await new PerceptoXDialog { XamlRoot = root, Title = UiText.T("SmartCleanupDialog.Text003"),
                    Content = DialogLayout.Section(UiText.T("SmartCleanupDialog.Text004"), selector), PrimaryButtonText = UiText.T("SmartCleanupDialog.Text005"), CloseButtonText = UiText.T("BatchMatchPage.Text012"),
                    DefaultButton = ContentDialogButton.Close }.ShowAsync() != ContentDialogResult.Primary) return;
                Guid id = (Guid)selector.SelectedItem;
                var recovery = await Task.Run(() => service.ReadRecoveryAsync(id));
                string preview = string.Join('\n', recovery.Entries.Select(entry =>
                    $"{entry.State}: {entry.Move.ArchivePath} → {entry.Move.OriginalPath}"));
                if (!await Confirm(root, UiText.T("SmartCleanupDialog.Text006"), preview + UiText.T("SmartCleanupDialog.Text007"))) return;
                result = await RunWithProgress(root, service, null, id);
            }
            else
            {
                var groups = model.Results.Select(row => row.Candidates.Where(candidate => candidate.IsSelected).ToArray())
                    .Where(group => group.Length >= 2).ToArray();
                if (groups.Length == 0) throw new InvalidOperationException(UiText.T("SmartCleanupDialog.Text008"));
                ComboBox rule = new() { Header = UiText.T("SmartCleanupDialog.Text009"), ItemsSource = new[] { UiText.T("SmartCleanupDialog.Text010"), UiText.T("SmartCleanupDialog.Text011") }, SelectedIndex = 0 };
                CheckBox verified = new() { Content = UiText.T("SmartCleanupDialog.Text012") };
                StackPanel content = new() { Spacing = 12, Width = 520 };
                content.Children.Add(DialogLayout.Section(UiText.T("SmartCleanupDialog.Text013"), DialogLayout.Text(UiText.T("SmartCleanupDialog.Text014", groups.Length))));
                content.Children.Add(DialogLayout.Section(UiText.T("SmartCleanupDialog.Text015"), rule, verified));
                if (await new PerceptoXDialog { XamlRoot = root, Title = UiText.T("SmartCleanupDialog.Text016"), Content = content,
                    PrimaryButtonText = UiText.T("SmartCleanupDialog.Text017"), CloseButtonText = UiText.T("BatchMatchPage.Text012"), DefaultButton = ContentDialogButton.Close }.ShowAsync() != ContentDialogResult.Primary) return;
                if (verified.IsChecked != true) throw new InvalidOperationException(UiText.T("SmartCleanupDialog.Text018"));
                var verifiedGroups = groups.Select(group => new VerifiedCleanupGroup(group.Select(candidate =>
                    new CleanupFileCandidate(candidate.Image.FilePath, candidate.Image.OriginalWidth,
                        candidate.Image.OriginalHeight, candidate.Image.OriginalLastWriteTimeUtc)).ToArray(), true)).ToArray();
                var keeperRule = rule.SelectedIndex == 0 ? KeeperRule.HighestResolution : KeeperRule.Newest;
                var plan = await Task.Run(() => service.PreviewAsync(verifiedGroups, keeperRule));
                string preview = UiText.T("SmartCleanupDialog.Text019") + string.Join('\n', plan.Keepers.Select(file => file.Path)) +
                    UiText.T("SmartCleanupDialog.Text020") + string.Join('\n', plan.Moves.Select(move => $"{move.OriginalPath} → {move.ArchivePath}"));
                if (!await Confirm(root, UiText.T("SmartCleanupDialog.Text021"), preview +
                    UiText.T("SmartCleanupDialog.Text022"))) return;
                result = await RunWithProgress(root, service, plan, null);
            }
            if (result.IndexRequiresRefresh) model.NotifyLibraryChanged();
            string message = UiText.T("SmartCleanupDialog.Text023", result.JournalPath) + string.Join('\n', result.Entries.Select(entry =>
                $"{entry.State}: {entry.Move.OriginalPath}" + (entry.Error is null ? "" : " · " + entry.Error))) +
                UiText.T("SmartCleanupDialog.Text024");
            await Notice(root, UiText.T("SmartCleanupDialog.Text025"), message);
        }
        catch (Exception error) { await Notice(root, UiText.T("SmartCleanupDialog.Text026"), error.Message); }
        finally { model.Workspace.EndExternalOperation(); }
    }

    private static async Task<CleanupResult> RunWithProgress(XamlRoot root, RecoverableSmartCleanupService service,
        SmartCleanupPlan? plan, Guid? recoveryId)
    {
        using CancellationTokenSource cancellation = new();
        TextBlock progressText = new() { Text = UiText.T("SmartCleanupDialog.Text027"), TextWrapping = TextWrapping.Wrap };
        StackPanel content = new() { Spacing = 12, Width = 480 };
        content.Children.Add(new ProgressBar { IsIndeterminate = true });
        content.Children.Add(progressText);
        ContentDialog dialog = new PerceptoXDialog() { XamlRoot = root, Title = UiText.T("SmartCleanupDialog.Text028"),
            Content = DialogLayout.Section("Progres", content), CloseButtonText = UiText.T("SmartCleanupDialog.Text029"), DefaultButton = ContentDialogButton.Close };
        bool finished = false;
        dialog.Closing += (_, args) =>
        {
            if (finished) return;
            args.Cancel = true;
            cancellation.Cancel();
            progressText.Text = UiText.T("SmartCleanupDialog.Text030");
        };
        var shown = dialog.ShowAsync();
        int completed = 0;
        Progress<CleanupEntryResult> progress = new(_ =>
        {
            completed++;
            if (!finished && !cancellation.IsCancellationRequested)
                progressText.Text = UiText.T("SmartCleanupDialog.Text031", completed, plan!.Moves.Count);
        });
        try
        {
            return await Task.Run(() => plan is not null
                ? service.ExecuteAsync(plan, progress, cancellation.Token)
                : service.UndoAsync(recoveryId!.Value, cancellation.Token));
        }
        finally { finished = true; dialog.Hide(); await shown; }
    }

    private static async Task<bool> Confirm(XamlRoot root, string title, string message) =>
        await new PerceptoXDialog { XamlRoot = root, Title = title,
            Content = new ScrollViewer { MaxHeight = 400, Content = DialogLayout.Section(UiText.T("SmartCleanupDialog.Text032"), new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }) },
            PrimaryButtonText = UiText.T("SmartCleanupDialog.Text033"), CloseButtonText = UiText.T("BatchMatchPage.Text012"), DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    private static async Task Notice(XamlRoot root, string title, string message) =>
        await new PerceptoXDialog { XamlRoot = root, Title = title,
            Content = new ScrollViewer { MaxHeight = 400, Content = DialogLayout.Section(UiText.T("SmartCleanupDialog.Text034"), new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }) },
            CloseButtonText = UiText.T("SmartCleanupDialog.Text035") }.ShowAsync();
}
