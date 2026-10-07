using PerceptoX.Application.Imaging;
using PerceptoX.Core.Fingerprints;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PerceptoX.Core.Images;

namespace PerceptoX.Infrastructure.Imaging;

public sealed class ImageSharpImageProcessor : IImageProcessor, IDisposable
{
    private const long DefaultBudgetBytes = 512L * 1024 * 1024;
    private readonly ImageSharpFingerprintExtractor _extractor;
    private readonly SemaphoreSlim _workers;
    private readonly DecodedMemoryBudget _memory;
    private readonly int _maximumDecodeSide;
    private readonly OptionalMagickDecoder? _modernDecoder;
    private readonly SemaphoreSlim _externalWorker = new(1, 1);

    public ImageSharpImageProcessor(
        IEnumerable<IImageFingerprintAlgorithm>? algorithms = null,
        int maxConcurrency = 2,
        long decodedMemoryBudgetBytes = DefaultBudgetBytes,
        int maximumDecodeSide = 0,
        OptionalMagickDecoder? modernDecoder = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDecodeSide);
        _maximumDecodeSide = maximumDecodeSide;
        _modernDecoder = modernDecoder;
        IImageFingerprintAlgorithm[] selected = algorithms?.ToArray() ??
            [new PerceptualHash64(), new DifferenceHash64(), new MultiRegionHash576()];
        _extractor = new ImageSharpFingerprintExtractor(selected, maximumDecodeSide);
        StringBuilder profile = new();
        foreach (IImageFingerprintAlgorithm algorithm in selected.OrderBy(
            item => item.Descriptor.AlgorithmId, StringComparer.Ordinal))
        {
            FingerprintDescriptor descriptor = algorithm.Descriptor;
            AppendPart(profile, descriptor.AlgorithmId);
            AppendPart(profile, descriptor.AlgorithmVersion.ToString(CultureInfo.InvariantCulture));
            AppendPart(profile, descriptor.ProfileId);
            AppendPart(profile, descriptor.BitLength.ToString(CultureInfo.InvariantCulture));
            AppendPart(profile, descriptor.SampleWidth.ToString(CultureInfo.InvariantCulture));
            AppendPart(profile, descriptor.SampleHeight.ToString(CultureInfo.InvariantCulture));
        }

        AppendPart(profile, ThumbnailCacheWriter.ProfileId);
        if (maximumDecodeSide > 0) AppendPart(profile, $"jpeg-targetsize-v1-{maximumDecodeSide}");
        if (modernDecoder is not null) AppendPart(profile, modernDecoder.RuntimeProfileId);
        ProcessingProfileId = "pipeline-sha256-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(profile.ToString())));
        _workers = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _memory = new DecodedMemoryBudget(decodedMemoryBudgetBytes);
    }

    public string ProcessingProfileId { get; }
    private string ModernSourceVersion(string version) => version.EndsWith("|" + _modernDecoder!.RuntimeProfileId, StringComparison.Ordinal)
        ? version : version + "|" + _modernDecoder.RuntimeProfileId;
    private static ThumbnailRecord RestoreSourceVersion(ThumbnailRecord thumbnail, string sourceVersion) =>
        new(thumbnail.ImageId, sourceVersion, thumbnail.ProfileId, thumbnail.RelativePath, thumbnail.FormatId,
            thumbnail.Width, thumbnail.Height);

    private static void AppendPart(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);

    public async Task<ImageProcessingResult> ProcessAsync(
        ImageProcessingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_modernDecoder?.SupportsExtension(Path.GetExtension(request.SourcePath)) == true)
        {
            await _externalWorker.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using OptionalMagickDecodedImage decoded = await _modernDecoder.DecodeToTemporaryPngAsync(
                    request.SourcePath, cancellationToken).ConfigureAwait(false);
                ImageProcessingResult result = await ProcessAsync(new ImageProcessingRequest(decoded.PngPath, request.ImageId,
                    ModernSourceVersion(request.SourceVersion), request.CacheRoot), cancellationToken).ConfigureAwait(false);
                return new ImageProcessingResult(result.Fingerprints, RestoreSourceVersion(result.Thumbnail, request.SourceVersion));
            }
            finally { _externalWorker.Release(); }
        }

        ImageInfo info = Image.Identify(
            new DecoderOptions { MaxFrames = 1, SkipMetadata = true }, request.SourcePath);
        long pixels = checked((long)info.Width * info.Height);
        if (pixels > ImageSharpFingerprintExtractor.MaximumPixels)
        {
            throw new InvalidDataException("Image exceeds the configured 100-megapixel safety limit.");
        }

        // Conservative admission estimate: decoded RGBA plus decoder/processing overhead.
        long estimatedBytes = checked(pixels * 8L + 1024 * 1024);
        await _workers.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using DecodedMemoryBudget.Lease lease =
                await _memory.AcquireAsync(estimatedBytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() => _extractor.Process(request, pixels, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _workers.Release();
        }
    }

    public void Dispose() { _workers.Dispose(); _externalWorker.Dispose(); }

    /// <summary>Restores a display artifact without recomputing or modifying stored fingerprints.</summary>
    public async Task<ThumbnailRecord> RegenerateThumbnailAsync(ImageProcessingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (_modernDecoder?.SupportsExtension(Path.GetExtension(request.SourcePath)) == true)
        {
            await _externalWorker.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using OptionalMagickDecodedImage decoded = await _modernDecoder.DecodeToTemporaryPngAsync(
                    request.SourcePath, cancellationToken).ConfigureAwait(false);
                ThumbnailRecord thumbnail = await RegenerateThumbnailAsync(new ImageProcessingRequest(decoded.PngPath, request.ImageId,
                    ModernSourceVersion(request.SourceVersion), request.CacheRoot), cancellationToken).ConfigureAwait(false);
                return RestoreSourceVersion(thumbnail, request.SourceVersion);
            }
            finally { _externalWorker.Release(); }
        }
        ImageInfo info = Image.Identify(new DecoderOptions { MaxFrames = 1, SkipMetadata = true }, request.SourcePath);
        long pixels = checked((long)info.Width * info.Height);
        if (pixels > ImageSharpFingerprintExtractor.MaximumPixels)
            throw new InvalidDataException("Image exceeds the configured 100-megapixel safety limit.");
        await _workers.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using DecodedMemoryBudget.Lease lease = await _memory.AcquireAsync(
                checked(pixels * 8L + 1024 * 1024), cancellationToken).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                using FileStream stream = new(request.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using Image<Rgba32> image = Image.Load<Rgba32>(ImageSharpFingerprintExtractor.DecodeOptions(info, _maximumDecodeSide), stream);
                if ((long)image.Width * image.Height > pixels)
                    throw new InvalidDataException("Image changed during thumbnail decoding.");
                cancellationToken.ThrowIfCancellationRequested();
                image.Mutate(context => context.AutoOrient().BackgroundColor(Color.White));
                return ThumbnailCacheWriter.Save(image, request, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _workers.Release(); }
    }
}
