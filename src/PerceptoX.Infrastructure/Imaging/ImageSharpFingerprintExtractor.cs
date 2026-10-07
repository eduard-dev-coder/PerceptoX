using PerceptoX.Application.Imaging;
using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;
using PerceptoX.Core.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace PerceptoX.Infrastructure.Imaging;

public sealed class ImageSharpFingerprintExtractor : IImageFingerprintExtractor
{
    internal const long MaximumPixels = 100_000_000;
    private readonly IImageFingerprintAlgorithm[] _algorithms;
    private readonly int _maximumDecodeSide;

    public ImageSharpFingerprintExtractor()
        : this([new PerceptualHash64(), new DifferenceHash64(), new MultiRegionHash576()])
    {
    }

    public ImageSharpFingerprintExtractor(IEnumerable<IImageFingerprintAlgorithm> algorithms, int maximumDecodeSide = 0)
    {
        ArgumentNullException.ThrowIfNull(algorithms);
        _algorithms = algorithms.ToArray();
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDecodeSide);
        _maximumDecodeSide = maximumDecodeSide;
        if (_algorithms.Length == 0)
        {
            throw new ArgumentException("At least one fingerprint algorithm is required.", nameof(algorithms));
        }
    }

    public ImageFingerprintSet Extract(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ExtractCore(path, null, long.MaxValue, CancellationToken.None).Fingerprints;
    }

    internal ImageProcessingResult Process(
        ImageProcessingRequest request,
        long admittedPixels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        (ImageFingerprintSet fingerprints, ThumbnailRecord? thumbnail) =
            ExtractCore(request.SourcePath, request, admittedPixels, cancellationToken);
        return new ImageProcessingResult(fingerprints, thumbnail!);
    }

    private (ImageFingerprintSet Fingerprints, ThumbnailRecord? Thumbnail) ExtractCore(
        string path,
        ImageProcessingRequest? request,
        long admittedPixels,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        ImageInfo info = Image.Identify(new DecoderOptions { MaxFrames = 1, SkipMetadata = true }, stream);
        if ((long)info.Width * info.Height > MaximumPixels || (long)info.Width * info.Height > admittedPixels)
        {
            throw new InvalidDataException("Image exceeds the configured 100-megapixel safety limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        DecoderOptions decodeOptions = DecodeOptions(info, _maximumDecodeSide);
        using Image<Rgba32> source = Image.Load<Rgba32>(decodeOptions, stream);
        if ((long)source.Width * source.Height > MaximumPixels ||
            (long)source.Width * source.Height > admittedPixels)
        {
            throw new InvalidDataException("Decoded image exceeds the configured 100-megapixel safety limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        bool swapDimensions = source.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var orientation) == true
            && orientation.Value is >= 5 and <= 8;
        source.Mutate(context => context.AutoOrient().BackgroundColor(Color.White));

        List<Fingerprint> fingerprints = new(_algorithms.Length);
        foreach (IImageFingerprintAlgorithm algorithm in _algorithms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FingerprintDescriptor descriptor = algorithm.Descriptor;
            using Image<Rgba32> working = source.Clone(context =>
                context.Resize(descriptor.SampleWidth, descriptor.SampleHeight, KnownResamplers.Bicubic)
                    .Grayscale(GrayscaleMode.Bt709));
            Fingerprint fingerprint = algorithm.Compute(CopyGrayPixels(working));
            if (fingerprint.Descriptor != descriptor)
            {
                throw new InvalidDataException("A fingerprint algorithm returned a different descriptor.");
            }

            fingerprints.Add(fingerprint);
        }

        ImageFingerprintSet result = new(swapDimensions ? info.Height : info.Width,
            swapDimensions ? info.Width : info.Height, fingerprints);
        ThumbnailRecord? thumbnail = request is null
            ? null
            : ThumbnailCacheWriter.Save(source, request, cancellationToken);
        return (result, thumbnail);
    }

    internal static DecoderOptions DecodeOptions(ImageInfo info, int maximumSide) =>
        maximumSide > 0 && info.Metadata.DecodedImageFormat?.Name == "JPEG"
            ? new DecoderOptions { MaxFrames = 1, TargetSize = new Size(maximumSide, maximumSide) }
            : new DecoderOptions { MaxFrames = 1 };

    private static byte[] CopyGrayPixels(Image<Rgba32> image)
    {
        byte[] pixels = new byte[checked(image.Width * image.Height)];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    pixels[y * accessor.Width + x] = row[x].R;
                }
            }
        });

        return pixels;
    }
}
