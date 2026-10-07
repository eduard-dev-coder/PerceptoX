using CoenM.ImageHash.HashAlgorithms;
using PerceptoX.Application.Matching;
using PerceptoX.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.Infrastructure.Tests;

public sealed class ImageHashOracleTests
{
    [Theory]
    [InlineData(0, 0x12047DD53F421FE1UL, 0x0103030303073EE6UL, 0x9135299A2A7A8E3FUL, 0xFEFCECECFCF8C119UL)]
    [InlineData(1, 0x09023B2A7F2A7F2AUL, 0x0A4D52A54AB54EADUL, 0x802A7D2ABD0A7FAAUL, 0xB592A55AB54AB152UL)]
    public void GeneratedImageMatchesPinnedPerceptoXAndImageHashVectors(
        int pattern,
        ulong expectedOurPerceptual,
        ulong expectedOurDifference,
        ulong expectedImageHashPerceptual,
        ulong expectedImageHashDifference)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, ".test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, $"pattern-{pattern}.png");
            using (Image<Rgba32> image = CreatePattern(pattern))
            {
                image.SaveAsPng(path);
            }

            ImageFingerprintSet ours = new ImageSharpFingerprintExtractor().Extract(path);
            Assert.True(ours.GetRequired("phash").Value.TryGetUInt64(out ulong ourPerceptual));
            Assert.True(ours.GetRequired("dhash").Value.TryGetUInt64(out ulong ourDifference));

            using Image<Rgba32> oraclePerceptualImage = Image.Load<Rgba32>(path);
            using Image<Rgba32> oracleDifferenceImage = Image.Load<Rgba32>(path);
            ulong imageHashPerceptual = new PerceptualHash().Hash(oraclePerceptualImage);
            ulong imageHashDifference = new DifferenceHash().Hash(oracleDifferenceImage);

            Assert.Equal(expectedOurPerceptual, ourPerceptual);
            Assert.Equal(expectedOurDifference, ourDifference);
            Assert.Equal(expectedImageHashPerceptual, imageHashPerceptual);
            Assert.Equal(expectedImageHashDifference, imageHashDifference);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Image<Rgba32> CreatePattern(int pattern)
    {
        Image<Rgba32> image = new(96, 64);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = pattern switch
                    {
                        0 => new Rgba32((byte)((x * 3 + y) % 256), (byte)((x + y * 4) % 256), (byte)((x * 5 + y * 2) % 256)),
                        _ => new Rgba32((byte)(((x / 12 + y / 8) % 2) * 180 + x % 60), (byte)((x * y + 37) % 256), (byte)((x * 7 + y * 11) % 256)),
                    };
                }
            }
        });

        return image;
    }
}
