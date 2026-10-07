using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Core.Tests;

public sealed class FingerprintTests
{
    [Fact]
    public void DifferenceHashRisingPixelsProducesZero()
    {
        byte[] pixels = new byte[9 * 8];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 9; x++)
            {
                pixels[y * 9 + x] = (byte)x;
            }
        }

        Fingerprint result = new DifferenceHash64().Compute(pixels);

        Assert.True(result.Value.TryGetUInt64(out ulong value));
        Assert.Equal(0UL, value);
    }

    [Fact]
    public void DifferenceHashFallingPixelsProducesAllSetBits()
    {
        byte[] pixels = new byte[9 * 8];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 9; x++)
            {
                pixels[y * 9 + x] = (byte)(8 - x);
            }
        }

        Fingerprint result = new DifferenceHash64().Compute(pixels);

        Assert.True(result.Value.TryGetUInt64(out ulong value));
        Assert.Equal(ulong.MaxValue, value);
    }

    [Fact]
    public void PerceptualHashConstantImagesProduceSameFingerprint()
    {
        PerceptualHash64 algorithm = new();
        Fingerprint dark = algorithm.Compute(new byte[32 * 32]);
        Fingerprint bright = algorithm.Compute(Enumerable.Repeat((byte)255, 32 * 32).ToArray());

        Assert.Equal(0, HammingDistance.Between(dark, bright).DifferingBits);
    }

    [Fact]
    public void PerceptualHashMatchesIndependentDirectDct()
    {
        byte[] pixels = new byte[32 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                pixels[y * 32 + x] = (byte)((x * 11 + y * 7 + x * y) % 256);
            }
        }

        Fingerprint actual = new PerceptualHash64().Compute(pixels);
        Assert.True(actual.Value.TryGetUInt64(out ulong actualValue));

        double[] coefficients = new double[64];
        for (int v = 0; v < 8; v++)
        {
            for (int u = 0; u < 8; u++)
            {
                double sum = 0;
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        sum += pixels[y * 32 + x]
                            * Math.Cos((2 * x + 1) * u * Math.PI / 64)
                            * Math.Cos((2 * y + 1) * v * Math.PI / 64);
                    }
                }

                double normalized = sum * (u == 0 ? 1d / Math.Sqrt(2) : 1d)
                    * (v == 0 ? 1d / Math.Sqrt(2) : 1d);
                coefficients[v * 8 + u] = Math.Abs(normalized) < 1e-8 ? 0 : normalized;
            }
        }

        double[] ac = coefficients[1..];
        Array.Sort(ac);
        double median = ac[ac.Length / 2];
        ulong expected = 0;
        for (int index = 1; index < coefficients.Length; index++)
        {
            if (coefficients[index] > median)
            {
                expected |= 1UL << (63 - index);
            }
        }

        Assert.Equal(expected, actualValue);
    }

    [Fact]
    public void MultiRegionHashHasStableShapeAndValue()
    {
        byte[] pixels = new byte[128 * 128];
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                pixels[y * 128 + x] = (byte)((x * 11 + y * 7 + x * y) & 255);
            }
        }

        Fingerprint result = new MultiRegionHash576().Compute(pixels);

        Assert.Equal(576, result.Value.BitLength);
        Assert.Equal(MultiRegionHash576.ByteLength, result.Value.Bytes.Length);
        Assert.Equal(
            "0CE79D34552B96C26DE7B63C49E02500B20CB21C9214D93A926292E494A6CBD7D" +
            "22A64AA49A993B39254245549499B9ADA6DF5995AA2645ADBFB4A06A5E324425" +
            "A00A422E7953CDB",
            result.Value.ToString());
    }

    [Fact]
    public void MultiRegionComparerFindsARegionIndependentOfItsSlot()
    {
        byte[] left = new byte[MultiRegionHash576.ByteLength];
        byte[] right = Enumerable.Repeat((byte)0xFF, MultiRegionHash576.ByteLength).ToArray();
        left.AsSpan(0, sizeof(ulong)).Fill(0x5A);
        right.AsSpan(3 * sizeof(ulong), sizeof(ulong)).Fill(0x5A);
        MultiRegionHashMatch match = MultiRegionHashComparer.Compare(left, right, 0);

        Assert.Equal(0, match.BestDistance);
        Assert.True(match.MatchedRegions >= 1);
    }

    [Fact]
    public void MultiRegionComparerRejectsInvalidLengthsAndThresholds()
    {
        byte[] valid = new byte[MultiRegionHash576.ByteLength];

        Assert.Throws<ArgumentException>(() => MultiRegionHashComparer.Compare(valid, new byte[1], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => MultiRegionHashComparer.Compare(valid, valid, 65));
    }

    [Fact]
    public void HammingDistanceRecognizesSignBitAndAllBits()
    {
        FingerprintDescriptor descriptor = new("test", 1, "profile-v1", 64, 1, 1);
        Fingerprint zero = new(descriptor, FingerprintValue.FromUInt64(0));
        Fingerprint signBit = new(descriptor, FingerprintValue.FromUInt64(1UL << 63));
        Fingerprint all = new(descriptor, FingerprintValue.FromUInt64(ulong.MaxValue));

        Assert.Equal(0, HammingDistance.Between(zero, zero).DifferingBits);
        Assert.Equal(1, HammingDistance.Between(zero, signBit).DifferingBits);
        Assert.Equal(64, HammingDistance.Between(zero, all).DifferingBits);
        Assert.Equal(0, HammingDistance.Between(zero, all).SimilarityPercent);
    }

    [Theory]
    [InlineData("other", 1, "profile-v1")]
    [InlineData("test", 2, "profile-v1")]
    [InlineData("test", 1, "profile-v2")]
    public void HammingDistanceRejectsIncompatibleDescriptors(string algorithm, int version, string profile)
    {
        Fingerprint left = new(
            new FingerprintDescriptor("test", 1, "profile-v1", 64, 1, 1),
            FingerprintValue.FromUInt64(0));
        Fingerprint right = new(
            new FingerprintDescriptor(algorithm, version, profile, 64, 1, 1),
            FingerprintValue.FromUInt64(0));

        Assert.Throws<ArgumentException>(() => HammingDistance.Between(left, right));
    }

    [Fact]
    public void FingerprintValueCopiesInputAndComparesVariableLengths()
    {
        byte[] input = [0b1000_0000, 0b0000_0000];
        FingerprintValue value = FingerprintValue.FromBytes(input, 9);
        input[0] = 0;

        Assert.Equal("8000", value.ToString());
        Assert.Equal(value, FingerprintValue.FromBytes([0b1000_0000, 0], 9));
        Assert.Throws<ArgumentException>(() => FingerprintValue.FromBytes([0, 0b0000_0001], 9));
    }

    [Fact]
    public void FingerprintValueRoundTripsUnsigned64()
    {
        foreach (ulong expected in new[] { 0UL, 1UL << 63, ulong.MaxValue })
        {
            FingerprintValue value = FingerprintValue.FromUInt64(expected);
            Assert.True(value.TryGetUInt64(out ulong actual));
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 1)]
    [InlineData(63, 8)]
    [InlineData(64, 8)]
    [InlineData(65, 9)]
    [InlineData(130, 17)]
    public void FingerprintValueSupportsVariableBitLengths(int bits, int byteCount)
    {
        FingerprintValue value = FingerprintValue.FromBytes(new byte[byteCount], bits);

        Assert.Equal(bits, value.BitLength);
        Assert.Equal(byteCount, value.Bytes.Length);
    }

    [Fact]
    public void DescriptorRejectsInvalidIdentifiersAndDimensions()
    {
        Assert.Throws<ArgumentException>(() => new FingerprintDescriptor("Bad ID", 1, "profile", 64, 8, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FingerprintDescriptor("test", 0, "profile", 64, 8, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FingerprintDescriptor("test", 1, "profile", 64, 0, 8));
    }
}
