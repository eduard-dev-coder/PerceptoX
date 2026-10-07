using System.Buffers.Binary;

namespace PerceptoX.Core.Fingerprints;

public sealed class MultiRegionHash576 : IImageFingerprintAlgorithm
{
    public const int RegionCount = 9;
    public const int RegionBitLength = 64;
    public const int ByteLength = RegionCount * sizeof(ulong);
    private const int SampleSize = 128;

    private static readonly Region[] Regions =
    [
        new(0, 0, 128, 128),
        new(16, 16, 96, 96),
        new(0, 0, 96, 96),
        new(32, 0, 96, 96),
        new(0, 32, 96, 96),
        new(32, 32, 96, 96),
        new(26, 26, 76, 76),
        new(13, 13, 102, 102),
        new(9, 13, 110, 102)
    ];

    public FingerprintDescriptor Descriptor { get; } = new(
        "multiregion",
        1,
        "imagesharp3.1.12-frame1-autoorient-white-bicubic-bt709-regions9-dhash64-v1",
        RegionCount * RegionBitLength,
        SampleSize,
        SampleSize);

    public Fingerprint Compute(ReadOnlySpan<byte> grayscalePixels)
    {
        if (grayscalePixels.Length != SampleSize * SampleSize)
        {
            throw new ArgumentException("Multi-region hash requires a 128x128 grayscale image.",
                nameof(grayscalePixels));
        }

        Span<byte> encoded = stackalloc byte[ByteLength];
        Span<byte> sample = stackalloc byte[9 * 8];
        for (int index = 0; index < Regions.Length; index++)
        {
            SampleRegion(grayscalePixels, Regions[index], sample);
            ulong hash = ComputeDifferenceHash(sample);
            BinaryPrimitives.WriteUInt64BigEndian(encoded[(index * sizeof(ulong))..], hash);
        }

        return new Fingerprint(Descriptor, FingerprintValue.FromBytes(encoded, Descriptor.BitLength));
    }

    private static void SampleRegion(ReadOnlySpan<byte> pixels, Region region, Span<byte> destination)
    {
        for (int y = 0; y < 8; y++)
        {
            double sourceY = region.Top + ((y + 0.5) * region.Height / 8d) - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, SampleSize - 1);
            int y1 = Math.Min(y0 + 1, SampleSize - 1);
            double fy = sourceY - y0;
            for (int x = 0; x < 9; x++)
            {
                double sourceX = region.Left + ((x + 0.5) * region.Width / 9d) - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, SampleSize - 1);
                int x1 = Math.Min(x0 + 1, SampleSize - 1);
                double fx = sourceX - x0;
                double top = pixels[y0 * SampleSize + x0] * (1d - fx) +
                    pixels[y0 * SampleSize + x1] * fx;
                double bottom = pixels[y1 * SampleSize + x0] * (1d - fx) +
                    pixels[y1 * SampleSize + x1] * fx;
                destination[y * 9 + x] = (byte)Math.Clamp(
                    (int)Math.Round(top * (1d - fy) + bottom * fy), 0, 255);
            }
        }
    }

    private static ulong ComputeDifferenceHash(ReadOnlySpan<byte> pixels)
    {
        ulong value = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int bit = y * 8 + x;
                if (pixels[y * 9 + x] > pixels[y * 9 + x + 1])
                {
                    value |= 1UL << (63 - bit);
                }
            }
        }

        return value;
    }

    private readonly record struct Region(int Left, int Top, int Width, int Height);
}
