using System.Security.Cryptography;
using PerceptoX.Application.Imaging;
using PerceptoX.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.Infrastructure.Tests;

public sealed class ColorProfileFixtureTests
{
    // External ICC profiles, CC0, pinned source and hashes in docs/COLOR_PROFILE_VALIDATION.md.
    // The pixel pattern and JPEGs are generated locally; no photographs are redistributed.
    [Theory]
    [InlineData(JpegEncodingColor.Rgb, "sRGB-v2-micro.icc", "Rgb",
        "0A8A33AEA66A6F154A5642EBE168EF287E73265D9F7B51C42A45E6EEDBACDA7A")]
    [InlineData(JpegEncodingColor.Cmyk, "CGATS001Compat-v2-micro.icc", "Cmyk",
        "73E1BA37D2BAD5BAB2A964F40A9EED96209666EFC067C3322626214BBEF234A0")]
    [InlineData(JpegEncodingColor.Ycck, "CGATS001Compat-v2-micro.icc", "Cmyk",
        "73E1BA37D2BAD5BAB2A964F40A9EED96209666EFC067C3322626214BBEF234A0")]
    public async Task ValidExternalIccSurvivesSourceEncodingAndPipelineStripsThumbnailMetadata(
        JpegEncodingColor encoding, string fixtureName, string expectedColorSpace, string expectedHash)
    {
        string fixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "ColorProfiles", fixtureName));
        byte[] profileBytes = File.ReadAllBytes(fixturePath);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(profileBytes)));
        IccProfile profile = new(profileBytes);
        Assert.True(profile.CheckIsValid());
        Assert.Equal(expectedColorSpace, profile.Header.DataColorSpace.ToString());
        Assert.NotEmpty(profile.Entries);
        string directory = Path.Combine(AppContext.BaseDirectory, ".test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string sourcePath = Path.Combine(directory, "profiled.jpg");
            using (Image<Rgba32> source = new(320, 160))
            {
                for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    source[x, y] = new Rgba32((byte)(x % 256), (byte)((y * 3) % 256),
                        (byte)((x + y) % 256));
                source.Metadata.IccProfile = profile;
                source.SaveAsJpeg(sourcePath, new JpegEncoder { ColorType = encoding, Quality = 90 });
            }
            byte[] originalBytes = File.ReadAllBytes(sourcePath);
            ImageInfo original = Image.Identify(sourcePath);
            Assert.Equal(encoding.ToString(), original.Metadata.GetJpegMetadata().ColorType.ToString());
            IccProfile embedded = Assert.IsType<IccProfile>(original.Metadata.IccProfile);
            Assert.True(embedded.CheckIsValid());
            Assert.Equal(expectedColorSpace, embedded.Header.DataColorSpace.ToString());
            Assert.Equal(profileBytes, embedded.ToByteArray());
            using ImageSharpImageProcessor processor = new();
            ImageProcessingRequest request = new(sourcePath, 1, "icc-fixture-v1", Path.Combine(directory, "thumbs"));
            ImageProcessingResult processed = await processor.ProcessAsync(request);
            Assert.Equal(320, processed.Fingerprints.Width);
            Assert.Equal(160, processed.Fingerprints.Height);
            Assert.Equal(3, processed.Fingerprints.Fingerprints.Count);
            Assert.Equal(8, processed.Fingerprints.GetRequired("phash").Value.Bytes.Length);
            Assert.Equal(8, processed.Fingerprints.GetRequired("dhash").Value.Bytes.Length);
            Assert.Equal(72, processed.Fingerprints.GetRequired("multiregion").Value.Bytes.Length);
            string thumbnailPath = Path.Combine(request.CacheRoot,
                processed.Thumbnail.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            using Image<Rgba32> thumbnail = Image.Load<Rgba32>(thumbnailPath);
            Assert.Equal(256, thumbnail.Width);
            Assert.Equal(128, thumbnail.Height);
            Assert.Null(thumbnail.Metadata.IccProfile);
            Assert.Null(thumbnail.Metadata.ExifProfile);
            Assert.Null(thumbnail.Metadata.XmpProfile);
            Assert.Null(thumbnail.Metadata.IptcProfile);
            Assert.Equal(originalBytes, File.ReadAllBytes(sourcePath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void InstalledImageSharpConfigurationHasNoHeicOrAvifDecoder()
    {
        string[] extensions = Configuration.Default.ImageFormats.SelectMany(format => format.FileExtensions)
            .ToArray();
        Assert.DoesNotContain(extensions, extension => extension.Equals("heic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensions, extension => extension.Equals("heif", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensions, extension => extension.Equals("avif", StringComparison.OrdinalIgnoreCase));
    }
}
