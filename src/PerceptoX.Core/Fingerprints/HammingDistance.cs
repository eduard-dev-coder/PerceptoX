using System.Numerics;

namespace PerceptoX.Core.Fingerprints;

public readonly record struct HammingDistance
{
    private HammingDistance(int differingBits, int bitLength)
    {
        DifferingBits = differingBits;
        BitLength = bitLength;
    }

    public int DifferingBits { get; }
    public int BitLength { get; }
    public double SimilarityPercent => BitLength > 0
        ? 100d * (1d - (double)DifferingBits / BitLength)
        : throw new InvalidOperationException("A default HammingDistance has no bit length.");

    public static HammingDistance Between(Fingerprint left, Fingerprint right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Descriptor != right.Descriptor)
        {
            throw new ArgumentException("Fingerprints with different algorithms or profiles are incompatible.", nameof(right));
        }

        int differentBits = 0;
        if (left.Value.TryGetUInt64(out ulong left64) && right.Value.TryGetUInt64(out ulong right64))
        {
            differentBits = BitOperations.PopCount(left64 ^ right64);
        }
        else
        {
            ReadOnlySpan<byte> leftBytes = left.Value.Bytes;
            ReadOnlySpan<byte> rightBytes = right.Value.Bytes;
            for (int index = 0; index < leftBytes.Length; index++)
            {
                differentBits += BitOperations.PopCount((uint)(leftBytes[index] ^ rightBytes[index]));
            }
        }

        return new HammingDistance(differentBits, left.Descriptor.BitLength);
    }
}
