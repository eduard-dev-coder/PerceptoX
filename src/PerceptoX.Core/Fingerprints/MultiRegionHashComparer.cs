using System.Buffers.Binary;
using System.Numerics;

namespace PerceptoX.Core.Fingerprints;

public readonly record struct MultiRegionHashMatch(
    int BestDistance,
    int LeftMatchedRegions,
    int RightMatchedRegions)
{
    public int MatchedRegions => Math.Min(LeftMatchedRegions, RightMatchedRegions);
    public double BestSimilarityPercent =>
        100d * (1d - (double)BestDistance / MultiRegionHash576.RegionBitLength);
}

public static class MultiRegionHashComparer
{
    public static MultiRegionHashMatch Compare(Fingerprint left, Fingerprint right, int maximumRegionDistance)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Descriptor != right.Descriptor ||
            left.Descriptor.AlgorithmId != "multiregion")
        {
            throw new ArgumentException("Multi-region fingerprints must use the same descriptor.", nameof(right));
        }

        return Compare(left.Value.Bytes, right.Value.Bytes, maximumRegionDistance);
    }

    public static MultiRegionHashMatch Compare(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right,
        int maximumRegionDistance)
    {
        if (left.Length != MultiRegionHash576.ByteLength || right.Length != MultiRegionHash576.ByteLength)
        {
            throw new ArgumentException("A multi-region fingerprint has an invalid byte length.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maximumRegionDistance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumRegionDistance, MultiRegionHash576.RegionBitLength);

        int bestDistance = MultiRegionHash576.RegionBitLength;
        int leftMatches = CountMatches(left, right, maximumRegionDistance, ref bestDistance);
        int rightMatches = CountMatches(right, left, maximumRegionDistance, ref bestDistance);
        return new MultiRegionHashMatch(bestDistance, leftMatches, rightMatches);
    }

    private static int CountMatches(
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> target,
        int maximumRegionDistance,
        ref int globalBest)
    {
        int matches = 0;
        for (int sourceIndex = 0; sourceIndex < MultiRegionHash576.RegionCount; sourceIndex++)
        {
            ulong sourceHash = ReadRegion(source, sourceIndex);
            int best = MultiRegionHash576.RegionBitLength;
            for (int targetIndex = 0; targetIndex < MultiRegionHash576.RegionCount; targetIndex++)
            {
                int distance = BitOperations.PopCount(sourceHash ^ ReadRegion(target, targetIndex));
                if (distance < best)
                {
                    best = distance;
                }
            }

            globalBest = Math.Min(globalBest, best);
            if (best <= maximumRegionDistance)
            {
                matches++;
            }
        }

        return matches;
    }

    private static ulong ReadRegion(ReadOnlySpan<byte> hash, int index) =>
        BinaryPrimitives.ReadUInt64BigEndian(hash[(index * sizeof(ulong))..]);
}
