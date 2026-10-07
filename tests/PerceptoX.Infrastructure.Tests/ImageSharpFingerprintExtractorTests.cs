using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;
using PerceptoX.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PerceptoX.Infrastructure.Tests;

public sealed class ImageSharpFingerprintExtractorTests
{
    [Fact]
    public void ExtractProducesEqualHashesForIdenticalFiles()
    {
        string directory = CreateTestDirectory();
        try
        {
            string path = Path.Combine(directory, "pattern.png");
            using (Image<Rgba32> image = CreatePattern())
            {
                image.SaveAsPng(path);
            }

            ImageComparisonResult result = new ImageComparisonService(new ImageSharpFingerprintExtractor())
                .Compare(path, path);

            Assert.Equal(0, result.PerceptualDistance.DifferingBits);
            Assert.Equal(0, result.DifferenceDistance.DifferingBits);
            Assert.Equal(100, result.BaselineSimilarityPercent);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExtractFindsResizedCopyWithinNearDuplicateRange()
    {
        string directory = CreateTestDirectory();
        try
        {
            string originalPath = Path.Combine(directory, "original.png");
            string resizedPath = Path.Combine(directory, "resized.png");
            using (Image<Rgba32> original = CreatePattern())
            {
                original.SaveAsPng(originalPath);
                using Image<Rgba32> resized = original.Clone(context =>
                    context.Resize(192, 128, KnownResamplers.Bicubic));
                resized.SaveAsPng(resizedPath);
            }

            ImageComparisonResult result = new ImageComparisonService(new ImageSharpFingerprintExtractor())
                .Compare(originalPath, resizedPath);

            Assert.True(result.PerceptualDistance.DifferingBits <= 16);
            Assert.True(result.DifferenceDistance.DifferingBits <= 16);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExtractFindsJpegRecompressedCopyWithinNearDuplicateRange()
    {
        string directory = CreateTestDirectory();
        try
        {
            string originalPath = Path.Combine(directory, "original.png");
            string compressedPath = Path.Combine(directory, "compressed.jpg");
            using (Image<Rgba32> image = CreatePattern())
            {
                image.SaveAsPng(originalPath);
                image.SaveAsJpeg(compressedPath);
            }

            ImageComparisonResult result = new ImageComparisonService(new ImageSharpFingerprintExtractor())
                .Compare(originalPath, compressedPath);

            Assert.True(result.PerceptualDistance.DifferingBits <= 16);
            Assert.True(result.DifferenceDistance.DifferingBits <= 16);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShouldFindCroppedImage()
    {
        string directory = CreateTestDirectory();
        try
        {
            string originalPath = Path.Combine(directory, "original.png");
            string croppedPath = Path.Combine(directory, "cropped.jpg");
            using (Image<Rgba32> sourceImage = CreatePattern())
            {
                sourceImage.SaveAsPng(originalPath);
                using Image<Rgba32> croppedImage = sourceImage.Clone(context =>
                    context.Crop(new Rectangle(12, 8, 72, 48)));
                croppedImage.SaveAsJpeg(croppedPath);
            }

            ImageSharpFingerprintExtractor extractor = new();
            Fingerprint original = extractor.Extract(originalPath).GetRequired("multiregion");
            Fingerprint cropped = extractor.Extract(croppedPath).GetRequired("multiregion");
            MultiRegionHashMatch match = MultiRegionHashComparer.Compare(original, cropped, 1);

            Assert.InRange(match.BestDistance, 0, 1);
            Assert.True(match.MatchedRegions >= 1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExtractFlattensTransparentPixelsOnWhite()
    {
        string directory = CreateTestDirectory();
        try
        {
            string transparentPath = Path.Combine(directory, "transparent.png");
            string whitePath = Path.Combine(directory, "white.png");
            using (Image<Rgba32> transparent = new(64, 64, new Rgba32(255, 0, 0, 0)))
            using (Image<Rgba32> white = new(64, 64, new Rgba32(255, 255, 255, 255)))
            {
                transparent.SaveAsPng(transparentPath);
                white.SaveAsPng(whitePath);
            }

            ImageComparisonResult result = new ImageComparisonService(new ImageSharpFingerprintExtractor())
                .Compare(transparentPath, whitePath);

            Assert.Equal(0, result.PerceptualDistance.DifferingBits);
            Assert.Equal(0, result.DifferenceDistance.DifferingBits);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExtractAcceptsAnAdditionalFingerprintAlgorithm()
    {
        string directory = CreateTestDirectory();
        try
        {
            string path = Path.Combine(directory, "pattern.png");
            using (Image<Rgba32> image = CreatePattern())
            {
                image.SaveAsPng(path);
            }

            ImageSharpFingerprintExtractor extractor = new(
                [new PerceptualHash64(), new DifferenceHash64(), new OnePixelTestAlgorithm()]);
            ImageFingerprintSet result = extractor.Extract(path);

            Assert.Equal(3, result.Fingerprints.Count);
            Assert.Equal("test", result.GetRequired("test").Descriptor.AlgorithmId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class OnePixelTestAlgorithm : IImageFingerprintAlgorithm
    {
        public FingerprintDescriptor Descriptor { get; } = new("test", 1, "white-bicubic-bt709", 1, 1, 1);

        public Fingerprint Compute(ReadOnlySpan<byte> grayscalePixels) => new(
            Descriptor,
            FingerprintValue.FromBytes([grayscalePixels[0] > 0 ? (byte)0b1000_0000 : (byte)0], 1));
    }

    private static Image<Rgba32> CreatePattern()
    {
        Image<Rgba32> image = new(96, 64);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(
                        (byte)((x * 3 + y) % 256),
                        (byte)((x + y * 4) % 256),
                        (byte)((x * 5 + y * 2) % 256));
                }
            }
        });

        return image;
    }

    private static string CreateTestDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, ".test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
