using System.Numerics;

namespace PerceptoX.Application.Matching;

public sealed class SimilaritySearchService
{
    private readonly IHammingSearchIndex _searchIndex;

    public SimilaritySearchService(IHammingSearchIndex? searchIndex = null) =>
        _searchIndex = searchIndex ?? new LinearHammingSearchIndex();

    public SimilaritySearchResult FindSimilarToSelectedImage(SearchIndexSnapshot snapshot,
        long queryImageId, SimilaritySearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new SimilaritySearchOptions();
        cancellationToken.ThrowIfCancellationRequested();
        if (!snapshot.TryGetById(queryImageId, out SearchIndexEntry query))
        {
            throw new KeyNotFoundException($"Image {queryImageId} is absent from the selected fingerprint profile.");
        }

        return FindBaseline(snapshot, query, queryImageId, queryImageId, options, cancellationToken);
    }

    public SimilaritySearchResult FindSimilarToExternalImage(
        SearchIndexSnapshot snapshot,
        MultiRegionSearchIndexSnapshot multiRegionSnapshot,
        ImageFingerprintSet queryFingerprints,
        long queryFileSize,
        SimilaritySearchOptions? options = null,
        MultiRegionSearchOptions? multiRegionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(multiRegionSnapshot);
        ArgumentNullException.ThrowIfNull(queryFingerprints);
        ArgumentOutOfRangeException.ThrowIfNegative(queryFileSize);
        EnsureCompatibleSnapshots(snapshot, multiRegionSnapshot);

        options ??= new SimilaritySearchOptions();
        multiRegionOptions ??= new MultiRegionSearchOptions(Math.Min(options.TopN * 4, 4000));
        if (!queryFingerprints.GetRequired("phash").Value.TryGetUInt64(out ulong perceptualHash) ||
            !queryFingerprints.GetRequired("dhash").Value.TryGetUInt64(out ulong differenceHash))
        {
            throw new InvalidDataException("External pHash and dHash values must contain exactly 64 bits.");
        }

        SearchIndexEntry query = new(0, perceptualHash, differenceHash,
            queryFingerprints.Width, queryFingerprints.Height, queryFileSize);
        SimilaritySearchResult baseline = FindBaseline(
            snapshot, query, queryImageId: 0, excludedImageId: null, options, cancellationToken);
        ReadOnlySpan<byte> regionalHash = queryFingerprints.GetRequired("multiregion").Value.Bytes;
        IReadOnlyList<MultiRegionSearchMatch> regional = MultiRegionSearchService.FindSimilar(
            multiRegionSnapshot, regionalHash, multiRegionOptions, cancellationToken);
        return MergeRegional(snapshot, baseline, query, regional, options.TopN);
    }

    private SimilaritySearchResult FindBaseline(
        SearchIndexSnapshot snapshot,
        SearchIndexEntry query,
        long queryImageId,
        long? excludedImageId,
        SimilaritySearchOptions options,
        CancellationToken cancellationToken)
    {
        PriorityQueue<SimilarityMatch, (double Score, long NegativeId)> best = new();
        _searchIndex.VisitCandidates(snapshot, query, options.MaxPerceptualDistance,
            (candidate, perceptualDistance) =>
            {
                if (candidate.ImageId == excludedImageId)
                {
                    return;
                }

                int differenceDistance = BitOperations.PopCount(query.DifferenceHash ^ candidate.DifferenceHash);
                if (differenceDistance > options.MaxDifferenceDistance)
                {
                    return;
                }

                double score = BaselineScore(query, candidate, perceptualDistance, differenceDistance);
                SimilarityMatch match = new(candidate.ImageId, perceptualDistance, differenceDistance, score);
                (double Score, long NegativeId) priority = (score, -candidate.ImageId);
                if (best.Count < options.TopN)
                {
                    best.Enqueue(match, priority);
                }
                else if (best.TryPeek(out _, out var worstPriority) && priority.CompareTo(worstPriority) > 0)
                {
                    best.Dequeue();
                    best.Enqueue(match, priority);
                }
            }, cancellationToken);

        SimilarityMatch[] matches = best.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(item => item.ScorePercent)
            .ThenBy(item => item.ImageId)
            .ToArray();
        return new SimilaritySearchResult(queryImageId, snapshot.ScanId,
            snapshot.ProcessingProfileId, matches);
    }

    public SimilaritySearchResult FindSimilarToSelectedImage(
        SearchIndexSnapshot snapshot,
        MultiRegionSearchIndexSnapshot multiRegionSnapshot,
        long queryImageId,
        SimilaritySearchOptions? options = null,
        MultiRegionSearchOptions? multiRegionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(multiRegionSnapshot);
        EnsureCompatibleSnapshots(snapshot, multiRegionSnapshot);

        options ??= new SimilaritySearchOptions();
        multiRegionOptions ??= new MultiRegionSearchOptions(Math.Min(options.TopN * 4, 4000));
        SimilaritySearchResult baseline = FindSimilarToSelectedImage(
            snapshot, queryImageId, options, cancellationToken);
        IReadOnlyList<MultiRegionSearchMatch> regional = MultiRegionSearchService.FindSimilar(
            multiRegionSnapshot, queryImageId, multiRegionOptions, cancellationToken);

        if (!snapshot.TryGetById(queryImageId, out SearchIndexEntry query))
        {
            throw new KeyNotFoundException($"Image {queryImageId} is absent from the selected fingerprint profile.");
        }

        return MergeRegional(snapshot, baseline, query, regional, options.TopN);
    }

    private static SimilaritySearchResult MergeRegional(
        SearchIndexSnapshot snapshot,
        SimilaritySearchResult baseline,
        SearchIndexEntry query,
        IReadOnlyList<MultiRegionSearchMatch> regional,
        int topN)
    {
        Dictionary<long, SimilarityMatch> merged = baseline.Matches.ToDictionary(match => match.ImageId);

        foreach (MultiRegionSearchMatch regionalMatch in regional)
        {
            if (!snapshot.TryGetById(regionalMatch.ImageId, out SearchIndexEntry candidate))
            {
                continue;
            }

            int perceptualDistance = BitOperations.PopCount(query.PerceptualHash ^ candidate.PerceptualHash);
            int differenceDistance = BitOperations.PopCount(query.DifferenceHash ^ candidate.DifferenceHash);
            double score = Math.Max(
                BaselineScore(query, candidate, perceptualDistance, differenceDistance),
                regionalMatch.ScorePercent);
            merged[regionalMatch.ImageId] = new SimilarityMatch(
                regionalMatch.ImageId,
                perceptualDistance,
                differenceDistance,
                score,
                regionalMatch.BestDistance,
                regionalMatch.MatchedRegions);
        }

        SimilarityMatch[] matches = merged.Values
            .OrderByDescending(item => item.ScorePercent)
            .ThenBy(item => item.ImageId)
            .Take(topN)
            .ToArray();
        return new SimilaritySearchResult(baseline.QueryImageId, snapshot.ScanId,
            snapshot.ProcessingProfileId, matches);
    }

    private static void EnsureCompatibleSnapshots(
        SearchIndexSnapshot snapshot,
        MultiRegionSearchIndexSnapshot multiRegionSnapshot)
    {
        if (snapshot.ScanId != multiRegionSnapshot.ScanId ||
            snapshot.ProcessingProfileId != multiRegionSnapshot.ProcessingProfileId)
        {
            throw new ArgumentException("Search snapshots must come from the same completed scan.",
                nameof(multiRegionSnapshot));
        }
    }

    public IEnumerable<SimilaritySearchResult> FindForSelectedImages(SearchIndexSnapshot snapshot,
        IEnumerable<long> queryImageIds, SimilaritySearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(queryImageIds);
        foreach (long imageId in queryImageIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return FindSimilarToSelectedImage(snapshot, imageId, options, cancellationToken);
        }
    }

    internal static double BaselineScore(SearchIndexEntry query, SearchIndexEntry candidate,
        int perceptualDistance, int differenceDistance)
    {
        double hashScore = 100d * (1d - (0.6d * perceptualDistance + 0.4d * differenceDistance) / 64d);
        double queryAspect = (double)query.Width / query.Height;
        double candidateAspect = (double)candidate.Width / candidate.Height;
        double aspectPenalty = Math.Min(5d, Math.Abs(Math.Log2(queryAspect / candidateAspect)) * 2d);
        double queryPixels = (double)query.Width * query.Height;
        double candidatePixels = (double)candidate.Width * candidate.Height;
        double resolutionPenalty = Math.Min(3d, Math.Abs(Math.Log2(queryPixels / candidatePixels)) * 0.5d);
        double fileSizePenalty = Math.Min(2d,
            Math.Abs(Math.Log2(((double)query.FileSize + 1d) / ((double)candidate.FileSize + 1d))) * 0.25d);
        return Math.Max(0d, hashScore - aspectPenalty - resolutionPenalty - fileSizePenalty);
    }
}
