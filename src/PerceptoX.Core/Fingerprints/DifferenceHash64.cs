namespace PerceptoX.Core.Fingerprints;

public sealed class DifferenceHash64 : IImageFingerprintAlgorithm
{
    public FingerprintDescriptor Descriptor { get; } = new(
        "dhash",
        1,
        "imagesharp3.1.12-frame1-autoorient-white-bicubic-bt709-left-gt-right-msb-v1",
        64,
        9,
        8);

    public Fingerprint Compute(ReadOnlySpan<byte> grayscalePixels)
    {
        if (grayscalePixels.Length != Descriptor.SampleWidth * Descriptor.SampleHeight)
        {
            throw new ArgumentException("dHash requires a 9x8 grayscale image.", nameof(grayscalePixels));
        }

        ulong value = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int bit = y * 8 + x;
                if (grayscalePixels[y * 9 + x] > grayscalePixels[y * 9 + x + 1])
                {
                    value |= 1UL << (63 - bit);
                }
            }
        }

        return new Fingerprint(Descriptor, FingerprintValue.FromUInt64(value));
    }
}
