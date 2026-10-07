using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Application.Matching;

public sealed record ImageComparisonResult(
    ImageFingerprintSet Left,
    ImageFingerprintSet Right,
    HammingDistance PerceptualDistance,
    HammingDistance DifferenceDistance)
{
    // This is an uncalibrated baseline, not a probability that images are duplicates.
    public double BaselineSimilarityPercent =>
        (PerceptualDistance.SimilarityPercent + DifferenceDistance.SimilarityPercent) / 2d;
}
