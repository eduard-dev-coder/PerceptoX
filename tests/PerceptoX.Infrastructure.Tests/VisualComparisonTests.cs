using PerceptoX.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
namespace PerceptoX.Infrastructure.Tests;

public sealed class VisualComparisonTests
{
    [Fact]
    public async Task MetadataDescribesOriginalFilesNotReducedPreview()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-metadata-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string left = Path.Combine(root, "referință cu spații.png"), right = Path.Combine(root, "original.png");
            using (Image<Rgba32> image = new(1600, 400, new Rgba32(20, 80, 140))) image.SaveAsPng(left);
            using (Image<Rgba32> image = new(400, 1600, new Rgba32(20, 80, 140))) image.SaveAsPng(right);
            using ComparisonPreview preview = await VisualComparisonService.CreateAsync(left, right, CancellationToken.None);
            Assert.Equal(new ComparisonImageMetadata(left, 1600, 400, new FileInfo(left).Length), preview.Reference);
            Assert.Equal(new ComparisonImageMetadata(right, 400, 1600, new FileInfo(right).Length), preview.Candidate);
            Assert.Equal("referință cu spații.png", preview.Reference.FileName);
            Assert.Equal(1024, preview.ReferenceWidth); Assert.Equal(256, preview.ReferenceHeight);
            Assert.Equal(256, preview.CandidateWidth); Assert.Equal(1024, preview.CandidateHeight);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CancelledPreparationDoesNotChangeSources()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-comparison-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "one.png");
            using (Image<Rgba32> image = new(1, 1, new Rgba32(20, 80, 140, 0))) image.SaveAsPng(source);
            byte[] before = File.ReadAllBytes(source);
            using CancellationTokenSource cancellation = new(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VisualComparisonService.CreateAsync(source, source, cancellation.Token));
            Assert.Equal(before, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OnePixelTransparentImageCanBeComparedWithoutChangingSource()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-comparison-tiny-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "one.png");
            using (Image<Rgba32> image = new(1, 1, new Rgba32(20, 80, 140, 0))) image.SaveAsPng(source);
            byte[] before = File.ReadAllBytes(source);
            using ComparisonPreview preview = await VisualComparisonService.CreateAsync(source, source, CancellationToken.None);
            Assert.Equal(1, preview.Reference.Width); Assert.Equal(1, preview.Reference.Height);
            Assert.Equal(1, preview.ReferenceWidth); Assert.Equal(1, preview.ReferenceHeight);
            Assert.True(preview.HasDifference);
            Assert.Equal(before, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OrientedPreviewKeepsEncodedSourceDimensionsInMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-comparison-exif-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "rotated.jpg");
            using (Image<Rgba32> image = new(120, 40, new Rgba32(20, 80, 140)))
            {
                image.Metadata.ExifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
                image.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, (ushort)6);
                image.SaveAsJpeg(source);
            }
            using ComparisonPreview preview = await VisualComparisonService.CreateAsync(source, source, CancellationToken.None);
            Assert.Equal(120, preview.Reference.Width); Assert.Equal(40, preview.Reference.Height);
            Assert.Equal(40, preview.ReferenceWidth); Assert.Equal(120, preview.ReferenceHeight);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task TexturedCropProducesEstimatedAlignedRegionWithoutChangingOriginal()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-crop-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "original.png"), query = Path.Combine(root, "crop.png");
            using (Image<Rgba32> image = new(128, 96))
            {
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++) image[x, y] = new Rgba32((byte)(x * 2), (byte)(y * 2), (byte)((x * y) % 255));
                image.SaveAsPng(original);
                using Image<Rgba32> crop = image.Clone(context => context.Crop(new Rectangle(32, 24, 64, 48)));
                crop.SaveAsPng(query);
            }
            byte[] before = File.ReadAllBytes(original);
            using var preview = await VisualComparisonService.CreateAsync(query, original, CancellationToken.None);
            Assert.True(preview.HasDifference);
            Assert.Contains("Regiune estimată", preview.AlignmentDescription, StringComparison.Ordinal);
            Assert.True(File.Exists(preview.AlignedCandidatePath));
            Assert.Equal(before, File.ReadAllBytes(original));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComparisonPreservesSourcesAndRejectsDifferentFraming(bool differentFraming)
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-comparison-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string left = Path.Combine(root, "reference.png"), right = Path.Combine(root, "original.png");
            using (Image<Rgba32> image = new(64, 40, new Rgba32(20, 80, 140))) image.SaveAsPng(left);
            using (Image<Rgba32> image = new(differentFraming ? 40 : 128, 80, new Rgba32(20, 80, 140))) image.SaveAsPng(right);
            byte[] original = File.ReadAllBytes(right);
            string previewFile;
            using (ComparisonPreview preview = await VisualComparisonService.CreateAsync(left, right, CancellationToken.None))
            {
                previewFile = preview.ReferencePath;
                Assert.Equal(!differentFraming, preview.HasDifference);
                Assert.True(File.Exists(preview.ReferencePath));
                Assert.True(File.Exists(preview.CandidatePath));
            }
            Assert.False(File.Exists(previewFile));
            Assert.Equal(original, File.ReadAllBytes(right));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
