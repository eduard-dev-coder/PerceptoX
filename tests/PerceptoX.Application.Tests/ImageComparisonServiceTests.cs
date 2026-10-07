using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Application.Tests;

public sealed class ImageComparisonServiceTests
{
    [Fact]
    public void CompareUsesBothAlgorithmsAndReturnsUncalibratedBaseline()
    {
        FingerprintDescriptor p = new("phash", 1, "test", 64, 32, 32);
        FingerprintDescriptor d = new("dhash", 1, "test", 64, 9, 8);
        ImageFingerprintSet left = new(100, 50,
        [
            new Fingerprint(p, FingerprintValue.FromUInt64(0)),
            new Fingerprint(d, FingerprintValue.FromUInt64(0))
        ]);
        ImageFingerprintSet right = new(50, 25,
        [
            new Fingerprint(p, FingerprintValue.FromUInt64(ulong.MaxValue)),
            new Fingerprint(d, FingerprintValue.FromUInt64(0))
        ]);
        ImageComparisonService service = new(new FakeExtractor(left, right));

        ImageComparisonResult result = service.Compare("left", "right");

        Assert.Equal(64, result.PerceptualDistance.DifferingBits);
        Assert.Equal(0, result.DifferenceDistance.DifferingBits);
        Assert.Equal(50, result.BaselineSimilarityPercent);
    }

    private sealed class FakeExtractor(ImageFingerprintSet left, ImageFingerprintSet right) : IImageFingerprintExtractor
    {
        public ImageFingerprintSet Extract(string path) => path == "left" ? left : right;
    }
}
