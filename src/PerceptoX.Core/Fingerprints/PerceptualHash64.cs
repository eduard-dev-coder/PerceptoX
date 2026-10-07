namespace PerceptoX.Core.Fingerprints;

public sealed class PerceptualHash64 : IImageFingerprintAlgorithm
{
    private const int SampleSize = 32;
    private const int LowFrequencySize = 8;
    private static readonly double[,] Cosine = BuildCosineTable();

    public FingerprintDescriptor Descriptor { get; } = new(
        "phash",
        1,
        "imagesharp3.1.12-frame1-autoorient-white-bicubic-bt709-dct32-low8-no-dc-msb-v1",
        64,
        SampleSize,
        SampleSize);

    public Fingerprint Compute(ReadOnlySpan<byte> grayscalePixels)
    {
        if (grayscalePixels.Length != SampleSize * SampleSize)
        {
            throw new ArgumentException("pHash requires a 32x32 grayscale image.", nameof(grayscalePixels));
        }

        Span<double> rowTerms = stackalloc double[SampleSize * LowFrequencySize];
        for (int y = 0; y < SampleSize; y++)
        {
            for (int u = 0; u < LowFrequencySize; u++)
            {
                double sum = 0;
                for (int x = 0; x < SampleSize; x++)
                {
                    sum += grayscalePixels[y * SampleSize + x] * Cosine[u, x];
                }

                rowTerms[y * LowFrequencySize + u] = sum;
            }
        }

        Span<double> coefficients = stackalloc double[64];
        for (int v = 0; v < LowFrequencySize; v++)
        {
            for (int u = 0; u < LowFrequencySize; u++)
            {
                double sum = 0;
                for (int y = 0; y < SampleSize; y++)
                {
                    sum += rowTerms[y * LowFrequencySize + u] * Cosine[v, y];
                }

                double coefficient = sum * (u == 0 ? 1d / Math.Sqrt(2) : 1d)
                    * (v == 0 ? 1d / Math.Sqrt(2) : 1d);
                coefficients[v * LowFrequencySize + u] = Math.Abs(coefficient) < 1e-8 ? 0 : coefficient;
            }
        }

        Span<double> ac = stackalloc double[63];
        coefficients[1..].CopyTo(ac);
        ac.Sort();
        double median = ac[ac.Length / 2];

        ulong value = 0;
        for (int index = 1; index < coefficients.Length; index++)
        {
            if (coefficients[index] > median)
            {
                value |= 1UL << (63 - index);
            }
        }

        return new Fingerprint(Descriptor, FingerprintValue.FromUInt64(value));
    }

    private static double[,] BuildCosineTable()
    {
        double[,] table = new double[LowFrequencySize, SampleSize];
        for (int frequency = 0; frequency < LowFrequencySize; frequency++)
        {
            for (int position = 0; position < SampleSize; position++)
            {
                table[frequency, position] = Math.Cos((2 * position + 1) * frequency * Math.PI / (2 * SampleSize));
            }
        }

        return table;
    }
}
