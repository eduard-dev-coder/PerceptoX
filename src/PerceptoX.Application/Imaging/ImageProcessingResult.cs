using PerceptoX.Application.Matching;
using PerceptoX.Core.Images;

namespace PerceptoX.Application.Imaging;

public sealed record ImageProcessingResult
{
    public ImageProcessingResult(ImageFingerprintSet fingerprints, ThumbnailRecord thumbnail)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        ArgumentNullException.ThrowIfNull(thumbnail);
        Fingerprints = fingerprints;
        Thumbnail = thumbnail;
    }

    public ImageFingerprintSet Fingerprints { get; }
    public ThumbnailRecord Thumbnail { get; }
}
