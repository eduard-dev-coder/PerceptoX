using System.Buffers.Binary;

namespace PerceptoX.Core.Fingerprints;

public sealed class FingerprintValue : IEquatable<FingerprintValue>
{
    private readonly byte[] _bytes;

    private FingerprintValue(byte[] bytes, int bitLength)
    {
        _bytes = bytes;
        BitLength = bitLength;
    }

    public int BitLength { get; }
    public ReadOnlySpan<byte> Bytes => _bytes;

    public static FingerprintValue FromUInt64(ulong value)
    {
        byte[] bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return new FingerprintValue(bytes, 64);
    }

    public static FingerprintValue FromBytes(ReadOnlySpan<byte> bytes, int bitLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitLength);

        if (bytes.Length != (bitLength + 7L) / 8L)
        {
            throw new ArgumentException("The byte count does not match the bit length.", nameof(bytes));
        }

        int paddingBits = (8 - bitLength % 8) % 8;
        if ((bytes[^1] & ((1 << paddingBits) - 1)) != 0)
        {
            throw new ArgumentException("Unused low bits in the final byte must be zero.", nameof(bytes));
        }

        return new FingerprintValue(bytes.ToArray(), bitLength);
    }

    public bool TryGetUInt64(out ulong value)
    {
        if (BitLength == 64)
        {
            value = BinaryPrimitives.ReadUInt64BigEndian(_bytes);
            return true;
        }

        value = default;
        return false;
    }

    public bool Equals(FingerprintValue? other) =>
        other is not null && BitLength == other.BitLength && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is FingerprintValue other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(BitLength);
        foreach (byte value in _bytes)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Convert.ToHexString(_bytes);
}
