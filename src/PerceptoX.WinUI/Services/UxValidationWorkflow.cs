#if DEBUG
using PerceptoX.Presentation.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.WinUI.Services;

/// <summary>
/// Deterministic, opt-in workload used only by Debug builds for the Phase 6 UX gate.
/// Activate it with --ux-validation; normal launches always use the real workflow.
/// </summary>
internal sealed class UxValidationWorkflow : IPerceptoXWorkflow
{
    private const int ResultCount = 1000;
    private static readonly JpegEncoder Encoder = new() { Quality = 82, SkipMetadata = true };

    public Task<IndexingSummary> IndexAsync(
        WorkspaceConfiguration workspace,
        CancellationToken cancellationToken) =>
        Task.FromResult(new IndexingSummary(1, ResultCount, ResultCount, 0, 0, 0));

    public Task<IReadOnlyList<SimilarImageItem>> FindSimilarAsync(
        SimilarityQuery query,
        CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<SimilarImageItem>>(
            () => CreateResults(query.Workspace.ThumbnailCacheRoot, cancellationToken),
            cancellationToken);

    public async Task<BatchMatchSummary> MatchFolderAsync(
        BatchMatchRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SimilarImageItem[] images = await Task.Run(() => CreateResults(request.Workspace.ThumbnailCacheRoot, cancellationToken), cancellationToken);
        BatchMatchItem[] items = images.Select((image, index) => new BatchMatchItem(
            Path.Combine(request.QueryRoot, $"reference-{index:D4}.jpg"), BatchMatchStatus.Found,
            image.ImageId, image.FilePath, image.ThumbnailUri, image.ScorePercent, 1,
            image.PerceptualDistance, image.DifferenceDistance, image.MultiRegionBestDistance, null)
        {
            QueryThumbnailUri = image.ThumbnailUri,
            Candidates = index % 4 == 0 ? [image, images[(index + 1) % images.Length]] : [image]
        }).ToArray();
        return new BatchMatchSummary(new IndexingSummary(1, ResultCount, ResultCount, 0, 0, 0),
            ResultCount, ResultCount, 0, 0, 0, 0, items);
    }

    private static SimilarImageItem[] CreateResults(string cacheRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheRoot);
        SimilarImageItem[] results = new SimilarImageItem[ResultCount];
        for (int index = 0; index < ResultCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string thumbnailPath = Path.Combine(cacheRoot, $"ux-{index:D4}.jpg");
            if (!File.Exists(thumbnailPath))
            {
                SaveThumbnail(thumbnailPath, index);
            }

            results[index] = new SimilarImageItem(
                index + 1,
                Path.Combine("C:\\PerceptoX-UX-Validation", $"image-{index:D4}.jpg"),
                new Uri(thumbnailPath),
                99d - (index % 500) / 10d,
                index % 16,
                index % 12,
                index % 10,
                9 - index % 4);
        }

        return results;
    }

    private static void SaveThumbnail(string path, int index)
    {
        byte red = (byte)(40 + index * 47 % 180);
        byte green = (byte)(40 + index * 71 % 180);
        byte blue = (byte)(40 + index * 97 % 180);
        using Image<Rgba32> image = new(256, 192, new Rgba32(red, green, blue));
        image.ProcessPixelRows(accessor =>
        {
            int stripeStart = 12 + index % 96;
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = stripeStart; x < row.Length; x += 48)
                {
                    int end = Math.Min(x + 12, row.Length);
                    row[x..end].Fill(new Rgba32(255 - red, 255 - green, 255 - blue));
                }
            }
        });
        image.SaveAsJpeg(path, Encoder);
    }
}
#endif
