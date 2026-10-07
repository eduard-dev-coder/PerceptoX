using PerceptoX.Infrastructure.Maintenance;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PerceptoX.Infrastructure.Imaging;

/// <summary>Bounded, read-only comparison. Does not interpret hash scores as geometric registration.</summary>
public static class VisualComparisonService
{
    public static Task<ComparisonPreview> CreateAsync(string reference, string candidate, CancellationToken token) =>
        CreateAsync(reference, candidate, null, token);

    public static Task<ComparisonPreview> CreateAsync(string reference, string candidate,
        OptionalMagickDecoder? modernDecoder, CancellationToken token) => Task.Run(async () =>
        {
            using OptionalMagickDecodedImage? decodedReference = modernDecoder?.SupportsExtension(Path.GetExtension(reference)) == true
                ? await modernDecoder.DecodeToTemporaryPngAsync(reference, token).ConfigureAwait(false) : null;
            using OptionalMagickDecodedImage? decodedCandidate = modernDecoder?.SupportsExtension(Path.GetExtension(candidate)) == true
                ? await modernDecoder.DecodeToTemporaryPngAsync(candidate, token).ConfigureAwait(false) : null;
            return Create(decodedReference?.PngPath ?? reference, decodedCandidate?.PngPath ?? candidate,
                reference, candidate, decodedReference, decodedCandidate, token);
        }, token);

    private static ComparisonPreview Create(string reference, string candidate, string referenceOriginal, string candidateOriginal,
        OptionalMagickDecodedImage? decodedReference, OptionalMagickDecodedImage? decodedCandidate, CancellationToken token)
    {
        using Image<Rgba32> left = Load(reference, token, out ImageInfo leftInfo);
        using Image<Rgba32> right = Load(candidate, token, out ImageInfo rightInfo);
        ComparisonImageMetadata referenceMetadata = Metadata(referenceOriginal, decodedReference?.OriginalWidth ?? leftInfo.Width, decodedReference?.OriginalHeight ?? leftInfo.Height);
        ComparisonImageMetadata candidateMetadata = Metadata(candidateOriginal, decodedCandidate?.OriginalWidth ?? rightInfo.Width, decodedCandidate?.OriginalHeight ?? rightInfo.Height);
        token.ThrowIfCancellationRequested();
        string directory = Path.Combine(Path.GetTempPath(), "PerceptoX-comparison", Guid.NewGuid().ToString("N"));
        ManagedPaths.RejectLinks(directory);
        Directory.CreateDirectory(directory);
        ManagedPaths.RejectLinks(directory);
        ComparisonPreview preview = new(directory, referenceMetadata, candidateMetadata, left.Width, left.Height, right.Width, right.Height);
        try
        {
            left.SaveAsPng(preview.ReferencePath);
            right.SaveAsPng(preview.CandidatePath);
            double ratio = (double)left.Width / left.Height / ((double)right.Width / right.Height);
            Rectangle? crop = CropAlignment.Find(left, right, token);
            if (crop is { } box)
            {
                right.Mutate(context => context.Crop(box));
                preview.AlignmentRegion = new(box.X, box.Y, box.Width, box.Height);
                preview.AlignmentDescription = $"Regiune estimată în originalul redus: x={box.X}, y={box.Y}, {box.Width}×{box.Height}. Aliniere experimentală translație/scalare; fără rotație sau perspectivă.";
            }
            else if (Math.Abs(ratio - 1) > .01)
            {
                preview.ExplanationKind = ComparisonExplanationKind.DifferentFraming;
                preview.Explanation = "Cadrare diferită: comparație vizuală disponibilă; heatmap refuzat fără aliniere geometrică verificată.";
                return preview;
            }
            right.Mutate(context => context.Resize(left.Width, left.Height));
            right.SaveAsPng(preview.AlignedCandidatePath);
            using Image<Rgba32> heatmap = new(left.Width, left.Height);
            long difference = 0;
            for (int y = 0; y < left.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < left.Width; x++)
                {
                    Rgba32 a = left[x, y], b = right[x, y];
                    int distance = (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3;
                    difference += distance;
                    // Green <= 12/channel, red above: visualization, not a probability or diagnosis.
                    heatmap[x, y] = distance <= 12 ? new Rgba32(20, 180, 90) : new Rgba32(230, 40, 40);
                }
            }
            double error = (double)difference / (left.Width * left.Height * 255d);
            if (error > .12)
            {
                preview.ExplanationKind = ComparisonExplanationKind.TooDifferent;
                preview.Explanation = "Diferența globală este prea mare pentru a presupune aceeași cadrare. Heatmap indisponibil; verificați imaginile vizual.";
                return preview;
            }
            heatmap.SaveAsPng(preview.DifferencePath);
            preview.HasDifference = true;
            preview.ExplanationKind = ComparisonExplanationKind.Heatmap;
            preview.MeanDifference = error;
            preview.Explanation = $"Heatmap experimental: verde ≤ 12/255 diferență medie RGB, roșu > 12/255. Eroare medie: {error:P1}. " + preview.AlignmentDescription;
            return preview;
        }
        catch { preview.Dispose(); throw; }
    }

    private static ComparisonImageMetadata Metadata(string path, int width, int height) =>
        new(Path.GetFullPath(path), width, height, new FileInfo(path).Length);

    private static Image<Rgba32> Load(string path, CancellationToken token, out ImageInfo info)
    {
        ManagedPaths.RejectLinks(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        info = Image.Identify(new DecoderOptions { MaxFrames = 1, SkipMetadata = true }, stream);
        if ((long)info.Width * info.Height > 32_000_000)
            throw new InvalidDataException("Comparația interactivă este limitată la 32 MP pe imagine pentru protejarea memoriei.");
        token.ThrowIfCancellationRequested();
        stream.Position = 0;
        Image<Rgba32> image = Image.Load<Rgba32>(new DecoderOptions { MaxFrames = 1 }, stream);
        try
        {
            if ((long)image.Width * image.Height > 32_000_000)
                throw new InvalidDataException("Imaginea s-a modificat în timpul decodării.");
            image.Mutate(context => context.AutoOrient().BackgroundColor(Color.White));
            // Do not upscale small sources: interpolation adds no detail and wastes alignment work.
            if (image.Width > 1024 || image.Height > 1024)
                image.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(1024, 1024), Mode = ResizeMode.Max }));
            return image;
        }
        catch { image.Dispose(); throw; }
    }
}

public sealed class ComparisonPreview : IDisposable
{
    internal ComparisonPreview(string directory, ComparisonImageMetadata reference, ComparisonImageMetadata candidate,
        int referenceWidth, int referenceHeight, int candidateWidth, int candidateHeight)
    {
        DirectoryPath = directory;
        Reference = reference; Candidate = candidate;
        ReferenceWidth = referenceWidth; ReferenceHeight = referenceHeight;
        CandidateWidth = candidateWidth; CandidateHeight = candidateHeight;
    }
    private string DirectoryPath { get; }
    public ComparisonImageMetadata Reference { get; }
    public ComparisonImageMetadata Candidate { get; }
    public int ReferenceWidth { get; }
    public int ReferenceHeight { get; }
    public int CandidateWidth { get; }
    public int CandidateHeight { get; }
    public string ReferencePath => Path.Combine(DirectoryPath, "reference.png");
    public string CandidatePath => Path.Combine(DirectoryPath, "candidate.png");
    public string DifferencePath => Path.Combine(DirectoryPath, "difference.png");
    public string AlignedCandidatePath => Path.Combine(DirectoryPath, "aligned.png");
    public string AlignmentDescription { get; internal set; } = "Aceeași cadrare presupusă; verificați vizual.";
    public ComparisonExplanationKind ExplanationKind { get; internal set; }
    public ComparisonRegion? AlignmentRegion { get; internal set; }
    public double MeanDifference { get; internal set; }
    public bool HasDifference { get; internal set; }
    public string Explanation { get; internal set; } = "";
    public void Dispose()
    {
        // Exact owned files only; no recursive deletion of a caller-controlled directory.
        foreach (string path in new[] { ReferencePath, CandidatePath, DifferencePath, AlignedCandidatePath })
            try { ManagedPaths.RejectLinks(path); File.Delete(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        try { Directory.Delete(DirectoryPath, recursive: false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

public enum ComparisonExplanationKind { DifferentFraming, TooDifferent, Heatmap }
public sealed record ComparisonRegion(int X, int Y, int Width, int Height);

/// <summary>Source-file metadata, not the dimensions or size of the reduced comparison PNG.</summary>
public sealed record ComparisonImageMetadata(string FilePath, int Width, int Height, long FileSize)
{
    public string FileName => Path.GetFileName(FilePath);
}
