#if DEBUG
using System.Text.Json;
using Microsoft.UI.Xaml;
using PerceptoX.Presentation.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PerceptoX.WinUI.Services;

/// <summary>Documentation-only driver of the real workflows; no synthetic matches or user workspace data.</summary>
internal static class DocumentationCapture
{
    private static readonly JsonSerializerOptions ProvenanceJson = new() { WriteIndented = true };
    internal static async Task SaveAsync(MainWindow window, ShellViewModel shell, string demoRoot, string destination)
    {
        demoRoot = Path.GetFullPath(demoRoot);
        string originals = Path.Combine(demoRoot, "Originals");
        string references = Path.Combine(demoRoot, "References");
        string original = Path.Combine(originals, "Earth-Blue-Marble.jpg");
        if (!File.Exists(original)) throw new FileNotFoundException("Prepare the attributed documentation demo first.", original);
        Directory.CreateDirectory(references);
        Directory.CreateDirectory(destination);
        string reference = Path.Combine(references, "Earth-thumbnail.jpg");
        using (Image image = await Image.LoadAsync(original))
        {
            image.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(480, 480), Mode = ResizeMode.Max }));
            await image.SaveAsJpegAsync(reference, new JpegEncoder { Quality = 85 });
            await image.SaveAsJpegAsync(Path.Combine(originals, "Earth-small.jpg"), new JpegEncoder { Quality = 92 });
        }
        File.Copy(original, Path.Combine(originals, "Earth-identical-copy.jpg"), overwrite: true);
        shell.Workspace.LibraryRoot = originals;
        shell.Workspace.DatabasePath = Path.Combine(destination, "demo-index.db");
        shell.Workspace.ThumbnailCacheRoot = Path.Combine(destination, "thumbs");
        shell.BatchMatch.QueryRoot = references;
        await shell.BatchMatch.MatchCommand.ExecuteAsync(null);
        if (shell.BatchMatch.LastSummary is not { Failed: 0, TotalQueries: > 0 } summary ||
            summary.Items.All(item => item.Candidates.Count == 0))
            throw new InvalidOperationException("Real demo matching failed; no screenshot will imply a successful match.");
        shell.BatchMatch.SelectBestCommand.Execute(null);
        FrameworkElement root = (FrameworkElement)window.Content;
        window.ShowBatchPreview();
        await DesignPreviewCapture.SaveAsync(root, Path.Combine(destination, "identify"));
        await DesignPreviewCapture.SaveDialogAsync(root, Path.Combine(destination, "comparison"),
            () => ImageComparisonDialog.ShowAsync(reference, original, root));
        await DesignPreviewCapture.SaveDialogAsync(root, Path.Combine(destination, "scan"),
            () => ScanProgressDialog.ShowAsync(shell.BatchMatch, root));
        shell.Groups.FolderText = originals;
        shell.Groups.AddFolderCommand.Execute(null);
        await shell.Groups.AnalyzeCommand.ExecuteAsync(null);
        if (shell.Groups.LastRun is not { Failed: 0, Groups: > 0 } grouping)
            throw new InvalidOperationException("Real demo grouping failed.");
        window.ShowPagePreview("groups");
        await DesignPreviewCapture.SaveAsync(root, Path.Combine(destination, "groups"));
        window.ShowSettings();
        await DesignPreviewCapture.SaveAsync(root, Path.Combine(destination, "settings"));
        await File.WriteAllTextAsync(Path.Combine(destination, "capture-provenance.json"), JsonSerializer.Serialize(new
        {
            Schema = 1, CapturedAtUtc = DateTime.UtcNow, Language = "en", Theme = ThemeManager.Current.ToString(),
            Renderer = "Actual running WinUI XAML RenderTargetBitmap; not an AI image or fabricated UI mockup.",
            MatchingWorkflow = "DesktopPerceptoXWorkflow", GroupingWorkflow = "DesktopGroupingWorkflow",
            Summary = new { summary.TotalQueries, summary.Found, summary.NearIdentical, summary.Ambiguous, summary.NotFound, summary.Failed },
            Groups = new { grouping.Images, grouping.Groups, grouping.Members, grouping.Failed },
            Source = "NASA Goddard Space Flight Center, Blue Marble; see website/assets/SCREENSHOTS.md",
            Demo = "One attributed Earth image, resized/recompressed variants and an exact file copy. This is not an accuracy benchmark."
        }, ProvenanceJson));
    }
}
#endif
