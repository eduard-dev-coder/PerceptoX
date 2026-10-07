using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Application.Matching;

public sealed class ImageComparisonService(IImageFingerprintExtractor extractor)
{
    public ImageComparisonResult Compare(string leftPath, string rightPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leftPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightPath);

        ImageFingerprintSet left = extractor.Extract(leftPath);
        ImageFingerprintSet right = extractor.Extract(rightPath);

        return new ImageComparisonResult(
            left,
            right,
            HammingDistance.Between(left.GetRequired("phash"), right.GetRequired("phash")),
            HammingDistance.Between(left.GetRequired("dhash"), right.GetRequired("dhash")));
    }
}
