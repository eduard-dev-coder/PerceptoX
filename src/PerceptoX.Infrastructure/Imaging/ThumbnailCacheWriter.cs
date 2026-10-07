using System.Security.Cryptography;
using System.Text;
using PerceptoX.Application.Imaging;
using PerceptoX.Core.Images;
using PerceptoX.Infrastructure.Maintenance;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PerceptoX.Infrastructure.Imaging;

internal static class ThumbnailCacheWriter
{
    private const int MaximumSide = 256;
    private static readonly JpegEncoder Encoder = new() { Quality = 85, SkipMetadata = true };
    internal const string ProfileId = "imagesharp3.1.12-jpeg256-bicubic-white-q85-v1";

    public static ThumbnailRecord Save(
        Image<Rgba32> source,
        ImageProcessingRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GeneratedArtifacts.MarkCache(request.CacheRoot);
        string versionDigest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{ProfileId}\0{request.SourceVersion}")));
        string relativePath = $"{versionDigest[..2]}/{request.ImageId}-{versionDigest}.jpg";
        string destination = Path.Combine(request.CacheRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        ManagedPaths.RejectLinks(destination);

        double scale = Math.Min(1d, (double)MaximumSide / Math.Max(source.Width, source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero));
        ThumbnailRecord record = new(
            request.ImageId, request.SourceVersion, ProfileId, relativePath, "jpeg", width, height);

        if (File.Exists(destination))
        {
            return record;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{request.ImageId}-{Guid.NewGuid():N}.tmp");

        try
        {
            using (Image<Rgba32> thumbnail = scale < 1d
                ? source.Clone(context => context.Resize(width, height, KnownResamplers.Bicubic))
                : source.Clone())
            using (FileStream output = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                cancellationToken.ThrowIfCancellationRequested();
                thumbnail.Metadata.ExifProfile = null;
                thumbnail.Metadata.IccProfile = null;
                thumbnail.Metadata.IptcProfile = null;
                thumbnail.Metadata.XmpProfile = null;
                thumbnail.SaveAsJpeg(output, Encoder);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, destination);
            }
            catch (IOException) when (File.Exists(destination))
            {
                // Another worker published the same immutable cache entry first.
            }

            return record;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
