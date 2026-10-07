using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Application.Matching;

public sealed record MultiRegionSearchOptions
{
    public MultiRegionSearchOptions(int topN = 200, int maximumRegionDistance = 1, int minimumMatchedRegions = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topN, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(topN, 4000);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRegionDistance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRegionDistance, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumMatchedRegions, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            minimumMatchedRegions, MultiRegionHash576.RegionCount);
        TopN = topN;
        MaximumRegionDistance = maximumRegionDistance;
        MinimumMatchedRegions = minimumMatchedRegions;
    }

    public int TopN { get; }
    public int MaximumRegionDistance { get; }
    public int MinimumMatchedRegions { get; }
}

public readonly record struct MultiRegionSearchMatch(
    long ImageId,
    int BestDistance,
    int MatchedRegions,
    double ScorePercent);

public sealed class MultiRegionSearchService
{
    public static IReadOnlyList<MultiRegionSearchMatch> FindSimilar(
        MultiRegionSearchIndexSnapshot snapshot,
        long queryImageId,
        MultiRegionSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new MultiRegionSearchOptions();
        cancellationToken.ThrowIfCancellationRequested();
        if (!snapshot.TryGetHashById(queryImageId, out ReadOnlySpan<byte> queryHash))
        {
            throw new KeyNotFoundException($"Image {queryImageId} has no multi-region fingerprint.");
        }

        return FindSimilar(snapshot, queryHash, options, queryImageId, cancellationToken);
    }

    public static IReadOnlyList<MultiRegionSearchMatch> FindSimilar(
        MultiRegionSearchIndexSnapshot snapshot,
        ReadOnlySpan<byte> queryHash,
        MultiRegionSearchOptions? options = null,
        CancellationToken cancellationToken = default) =>
        FindSimilar(snapshot, queryHash, options, excludedImageId: null, cancellationToken);

    private static MultiRegionSearchMatch[] FindSimilar(
        MultiRegionSearchIndexSnapshot snapshot,
        ReadOnlySpan<byte> queryHash,
        MultiRegionSearchOptions? options,
        long? excludedImageId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (queryHash.Length != MultiRegionHash576.ByteLength)
        {
            throw new ArgumentException("The query multi-region hash has an invalid byte length.",
                nameof(queryHash));
        }

        options ??= new MultiRegionSearchOptions();
        cancellationToken.ThrowIfCancellationRequested();

        PriorityQueue<MultiRegionSearchMatch, (double Score, long NegativeId)> best = new();
        for (int index = 0; index < snapshot.Count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            long imageId = snapshot.GetImageId(index);
            if (imageId == excludedImageId)
            {
                continue;
            }

            MultiRegionHashMatch comparison = MultiRegionHashComparer.Compare(
                queryHash, snapshot.GetHash(index), options.MaximumRegionDistance);
            if (comparison.MatchedRegions < options.MinimumMatchedRegions)
            {
                continue;
            }

            double score = 80d + 15d * comparison.BestSimilarityPercent / 100d +
                5d * Math.Min(comparison.MatchedRegions, 3) / 3d;
            MultiRegionSearchMatch match = new(
                imageId, comparison.BestDistance, comparison.MatchedRegions, score);
            (double Score, long NegativeId) priority = (score, -imageId);
            if (best.Count < options.TopN)
            {
                best.Enqueue(match, priority);
            }
            else if (best.TryPeek(out _, out var worst) && priority.CompareTo(worst) > 0)
            {
                best.Dequeue();
                best.Enqueue(match, priority);
            }
        }

        return best.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(item => item.ScorePercent)
            .ThenBy(item => item.ImageId)
            .ToArray();
    }
}
