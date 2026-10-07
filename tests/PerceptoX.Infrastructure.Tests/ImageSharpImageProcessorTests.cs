using System.Buffers.Binary;
using PerceptoX.Application.Imaging;
using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;
using PerceptoX.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.Infrastructure.Tests;

public sealed class ImageSharpImageProcessorTests
{
    [Theory]
    [InlineData(SixLabors.ImageSharp.Formats.Jpeg.JpegEncodingColor.Cmyk)]
    [InlineData(SixLabors.ImageSharp.Formats.Jpeg.JpegEncodingColor.Ycck)]
    public async Task CmykJpegDoesNotCrashAndProducesThumbnail(
        SixLabors.ImageSharp.Formats.Jpeg.JpegEncodingColor encoding)
    {
        string directory = CreateTestDirectory();
        try
        {
            string path = Path.Combine(directory, "cmyk.jpg");
            using (Image<Rgba32> source = new(80, 40, new Rgba32(25, 70, 155)))
            {
                source.SaveAsJpeg(path, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { ColorType = encoding });
            }
            using ImageSharpImageProcessor processor = new();
            var request = new ImageProcessingRequest(path, 1, "v1", Path.Combine(directory, "thumbs"));
            var result = await processor.ProcessAsync(request);
            Assert.Equal(80, result.Fingerprints.Width);
            Assert.Null(Image.Identify(GetThumbnailPath(request, result)).Metadata.IccProfile);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task OptInJpegDownsamplingHasSeparateProfileAndPreservesOrientedOriginalDimensions()
    {
        string directory = CreateTestDirectory();
        try
        {
            string path = Path.Combine(directory, "large.jpg");
            using (Image<Rgba32> source = new(2048, 1024, new Rgba32(25, 70, 155)))
            {
                source.Metadata.ExifProfile = new ExifProfile();
                source.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
                source.SaveAsJpeg(path);
            }
            using ImageSharpImageProcessor standard = new();
            using ImageSharpImageProcessor experimental = new(maximumDecodeSide: 512);
            Assert.NotEqual(standard.ProcessingProfileId, experimental.ProcessingProfileId);
            var result = await experimental.ProcessAsync(new(path, 1, "v1", Path.Combine(directory, "thumbs")));
            Assert.Equal(1024, result.Fingerprints.Width);
            Assert.Equal(2048, result.Fingerprints.Height);
            Assert.True(result.Thumbnail.Height <= 256);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ProcessCreatesBoundedThumbnailAndSameFingerprintsAsExtract()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "source.png");
            using (Image<Rgba32> source = new(640, 320, new Rgba32(30, 80, 120)))
            {
                source.SaveAsPng(sourcePath);
            }

            ImageProcessingRequest request = new(sourcePath, 12, "source-v1", Path.Combine(directory, "thumbs"));
            using ImageSharpImageProcessor processor = new();
            ImageProcessingResult result = await processor.ProcessAsync(request);
            ImageFingerprintSet separatelyExtracted = new ImageSharpFingerprintExtractor().Extract(sourcePath);

            Assert.Equal(256, result.Thumbnail.Width);
            Assert.Equal(128, result.Thumbnail.Height);
            Assert.Equal("jpeg", result.Thumbnail.FormatId);
            Assert.Equal(
                separatelyExtracted.GetRequired("phash").Value,
                result.Fingerprints.GetRequired("phash").Value);
            Assert.Equal(
                separatelyExtracted.GetRequired("dhash").Value,
                result.Fingerprints.GetRequired("dhash").Value);

            string thumbnailPath = GetThumbnailPath(request, result);
            ImageInfo thumbnail = Image.Identify(thumbnailPath);
            Assert.Equal(256, thumbnail.Width);
            Assert.Equal(128, thumbnail.Height);
            Assert.Null(thumbnail.Metadata.ExifProfile);
            Assert.Single(Directory.EnumerateFiles(request.CacheRoot, "*.jpg", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(request.CacheRoot, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessDoesNotUpscaleAndFlattensTransparentPixelsOnWhite()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "transparent.png");
            using (Image<Rgba32> source = new(40, 20, new Rgba32(255, 0, 0, 0)))
            {
                source.SaveAsPng(sourcePath);
            }

            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using ImageSharpImageProcessor processor = new();
            ImageProcessingResult result = await processor.ProcessAsync(request);
            using Image<Rgba32> thumbnail = Image.Load<Rgba32>(GetThumbnailPath(request, result));

            Assert.Equal(40, thumbnail.Width);
            Assert.Equal(20, thumbnail.Height);
            Assert.True(thumbnail[20, 10].R >= 250);
            Assert.True(thumbnail[20, 10].G >= 250);
            Assert.True(thumbnail[20, 10].B >= 250);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessKeepsAnExtremeAspectRatioWithinThumbnailBounds()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "wide.png");
            using (Image<Rgba32> source = new(10_000, 1, new Rgba32(10, 20, 30)))
            {
                source.SaveAsPng(sourcePath);
            }

            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using ImageSharpImageProcessor processor = new();
            ImageProcessingResult result = await processor.ProcessAsync(request);
            ImageInfo thumbnail = Image.Identify(GetThumbnailPath(request, result));

            Assert.Equal(256, thumbnail.Width);
            Assert.Equal(1, thumbnail.Height);
            Assert.Equal(thumbnail.Width, result.Thumbnail.Width);
            Assert.Equal(thumbnail.Height, result.Thumbnail.Height);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptInputDoesNotPublishThumbnail()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "corrupt.png");
            File.WriteAllBytes(sourcePath, [0, 1, 2, 3]);
            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using ImageSharpImageProcessor processor = new();

            await Assert.ThrowsAsync<UnknownImageFormatException>(() => processor.ProcessAsync(request));
            Assert.False(Directory.Exists(request.CacheRoot));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OversizedHeaderIsRejectedBeforePixelDecode()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "oversized.bmp");
            byte[] header = new byte[54];
            header[0] = (byte)'B';
            header[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2), header.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), header.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14), 40);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18), 20_000);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22), 20_000);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(26), 1);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(28), 24);
            File.WriteAllBytes(sourcePath, header);

            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using ImageSharpImageProcessor processor = new();

            await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(request));
            Assert.False(Directory.Exists(request.CacheRoot));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAppliesExifOrientationAndStripsMetadata()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "rotated.jpg");
            using (Image<Rgba32> source = new(80, 40, new Rgba32(20, 80, 160)))
            {
                source.Metadata.ExifProfile = new ExifProfile();
                source.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
                source.SaveAsJpeg(sourcePath);
            }

            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using ImageSharpImageProcessor processor = new();
            ImageProcessingResult result = await processor.ProcessAsync(request);
            ImageInfo thumbnail = Image.Identify(GetThumbnailPath(request, result));

            Assert.Equal(40, result.Fingerprints.Width);
            Assert.Equal(80, result.Fingerprints.Height);
            Assert.Equal(40, thumbnail.Width);
            Assert.Equal(80, thumbnail.Height);
            Assert.Null(thumbnail.Metadata.ExifProfile);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessUsesImmutableVersionedCacheEntry()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "source.png");
            using (Image<Rgba32> source = new(60, 30, new Rgba32(10, 20, 30)))
            {
                source.SaveAsPng(sourcePath);
            }

            string cacheRoot = Path.Combine(directory, "thumbs");
            using ImageSharpImageProcessor processor = new();
            ImageProcessingRequest firstRequest = new(sourcePath, 1, "v1", cacheRoot);
            ImageProcessingResult first = await processor.ProcessAsync(firstRequest);
            string firstPath = GetThumbnailPath(firstRequest, first);
            DateTime oldTimestamp = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(firstPath, oldTimestamp);

            ImageProcessingResult repeated = await processor.ProcessAsync(firstRequest);
            ImageProcessingRequest nextRequest = new(sourcePath, 1, "v2", cacheRoot);
            ImageProcessingResult next = await processor.ProcessAsync(nextRequest);

            Assert.Equal(first.Thumbnail.RelativePath, repeated.Thumbnail.RelativePath);
            Assert.Equal(oldTimestamp, File.GetLastWriteTimeUtc(firstPath));
            Assert.NotEqual(first.Thumbnail.RelativePath, next.Thumbnail.RelativePath);
            Assert.True(File.Exists(GetThumbnailPath(nextRequest, next)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentWorkersPublishOnlyOneThumbnailForSameVersion()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "source.png");
            using (Image<Rgba32> source = new(256, 128, new Rgba32(10, 20, 30)))
            {
                source.SaveAsPng(sourcePath);
            }

            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using ImageSharpImageProcessor processor = new(maxConcurrency: 2);
            ImageProcessingResult[] results = await Task.WhenAll(
                processor.ProcessAsync(request), processor.ProcessAsync(request));

            Assert.Equal(results[0].Thumbnail.RelativePath, results[1].Thumbnail.RelativePath);
            Assert.Single(Directory.EnumerateFiles(request.CacheRoot, "*.jpg", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(request.CacheRoot, "*.tmp", SearchOption.AllDirectories));
            Assert.NotNull(Image.Identify(GetThumbnailPath(request, results[0])));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessRejectsTooSmallMemoryBudgetWithoutPublishing()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "source.png");
            using (Image<Rgba32> source = new(100, 100, new Rgba32(10, 20, 30)))
            {
                source.SaveAsPng(sourcePath);
            }

            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using ImageSharpImageProcessor processor = new(decodedMemoryBudgetBytes: 1024);

            await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(request));
            Assert.False(Directory.Exists(request.CacheRoot));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationBeforeProcessingDoesNotPublishThumbnail()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "source.png");
            using (Image<Rgba32> source = new(64, 64, new Rgba32(10, 20, 30)))
            {
                source.SaveAsPng(sourcePath);
            }

            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            using ImageSharpImageProcessor processor = new();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(request, cancellation.Token));
            Assert.False(Directory.Exists(request.CacheRoot));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationAfterHashingDoesNotPublishThumbnail()
    {
        string directory = CreateTestDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "source.png");
            using (Image<Rgba32> source = new(64, 64, new Rgba32(10, 20, 30)))
            {
                source.SaveAsPng(sourcePath);
            }

            ImageProcessingRequest request = new(sourcePath, 1, "v1", Path.Combine(directory, "thumbs"));
            using CancellationTokenSource cancellation = new();
            using ImageSharpImageProcessor processor = new([new CancelingAlgorithm(cancellation)]);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(request, cancellation.Token));
            Assert.False(Directory.Exists(request.CacheRoot));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MemoryBudgetQueuesAndCancelsWaiters()
    {
        DecodedMemoryBudget budget = new(10);
        using DecodedMemoryBudget.Lease first = await budget.AcquireAsync(8, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        Task<DecodedMemoryBudget.Lease> waiting = budget.AcquireAsync(4, cancellation.Token).AsTask();

        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        using DecodedMemoryBudget.Lease second = await budget.AcquireAsync(2, CancellationToken.None);
        first.Dispose();
        using DecodedMemoryBudget.Lease third = await budget.AcquireAsync(4, CancellationToken.None);
    }

    [Fact]
    public async Task MemoryBudgetAdmitsQueuedWorkAfterRelease()
    {
        DecodedMemoryBudget budget = new(10);
        DecodedMemoryBudget.Lease first = await budget.AcquireAsync(8, CancellationToken.None);
        Task<DecodedMemoryBudget.Lease> waiting = budget.AcquireAsync(4, CancellationToken.None).AsTask();

        Assert.False(waiting.IsCompleted);
        first.Dispose();

        using DecodedMemoryBudget.Lease second = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(second);
    }

    private sealed class CancelingAlgorithm(CancellationTokenSource cancellation) : IImageFingerprintAlgorithm
    {
        public FingerprintDescriptor Descriptor { get; } = new("cancel-test", 1, "v1", 1, 1, 1);

        public Fingerprint Compute(ReadOnlySpan<byte> grayscalePixels)
        {
            cancellation.Cancel();
            return new Fingerprint(Descriptor, FingerprintValue.FromBytes([0], 1));
        }
    }

    private static string GetThumbnailPath(ImageProcessingRequest request, ImageProcessingResult result) =>
        Path.Combine(request.CacheRoot, result.Thumbnail.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string CreateTestDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, ".test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
