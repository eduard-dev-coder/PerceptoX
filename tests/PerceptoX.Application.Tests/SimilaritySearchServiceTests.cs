using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Application.Tests;

public sealed class SimilaritySearchServiceTests
{
    [Fact]
    public void MultiRegionDefaultsUseMeasuredConservativeThreshold()
    {
        MultiRegionSearchOptions options = new();

        Assert.Equal(1, options.MaximumRegionDistance);
        Assert.Equal(1, options.MinimumMatchedRegions);
    }

    [Fact]
    public void TopNIsBoundedSortedAndExcludesSelectedImage()
    {
        SearchIndexSnapshot snapshot = CreateSnapshot(
            new(1, 0, 0, 100, 100, 1000),
            new(4, 0, 0, 100, 100, 1000),
            new(3, 0, 0, 100, 100, 1000),
            new(2, 1, 0, 100, 100, 1000));

        SimilaritySearchResult result = new SimilaritySearchService().FindSimilarToSelectedImage(
            snapshot, 1, new SimilaritySearchOptions(topN: 2));

        Assert.Equal([3L, 4L], result.Matches.Select(match => match.ImageId));
        Assert.All(result.Matches, match => Assert.Equal(100d, match.ScorePercent));
        Assert.False(result.IsCalibrated);
    }

    [Fact]
    public void StrictRadiiCanBeBroadenedWithoutChangingSnapshot()
    {
        SearchIndexSnapshot snapshot = CreateSnapshot(
            new(1, 0, 0, 100, 100, 1000),
            new(2, 1, 1, 100, 100, 1000),
            new(3, ulong.MaxValue, ulong.MaxValue, 100, 100, 1000));
        SimilaritySearchService service = new();

        SimilaritySearchResult strict = service.FindSimilarToSelectedImage(snapshot, 1,
            new SimilaritySearchOptions(10, 0, 0));
        SimilaritySearchResult broad = service.FindSimilarToSelectedImage(snapshot, 1,
            SimilaritySearchOptions.Broad(10));

        Assert.Empty(strict.Matches);
        Assert.Equal(2, broad.Matches.Count);
        Assert.Equal(2, broad.Matches[0].ImageId);
    }

    [Fact]
    public void MetadataIsSoftAndCannotRemoveAHashIdenticalCandidate()
    {
        SearchIndexSnapshot snapshot = CreateSnapshot(
            new(1, 0, 0, 100, 100, 1000),
            new(2, 0, 0, 1000, 1, 1));

        SimilaritySearchResult result = new SimilaritySearchService().FindSimilarToSelectedImage(snapshot, 1,
            new SimilaritySearchOptions(10, 0, 0));

        SimilarityMatch match = Assert.Single(result.Matches);
        Assert.Equal(0, match.PerceptualDistance);
        Assert.Equal(0, match.DifferenceDistance);
        Assert.InRange(match.ScorePercent, 90d, 100d);
    }

    [Fact]
    public void BatchSearchIsLazyAndCancellationStopsTheNextQuery()
    {
        SearchIndexSnapshot snapshot = CreateSnapshot(
            new(1, 0, 0, 100, 100, 1000),
            new(2, 0, 0, 100, 100, 1000));
        using CancellationTokenSource cancellation = new();
        IEnumerator<SimilaritySearchResult> results = new SimilaritySearchService()
            .FindForSelectedImages(snapshot, [1, 2], cancellationToken: cancellation.Token)
            .GetEnumerator();

        Assert.True(results.MoveNext());
        Assert.Equal(1, results.Current.QueryImageId);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => results.MoveNext());
    }

    [Fact]
    public void MissingSelectedImageIsRejected()
    {
        SearchIndexSnapshot snapshot = CreateSnapshot(new SearchIndexEntry(1, 0, 0, 100, 100, 1000));

        Assert.Throws<KeyNotFoundException>(() =>
            new SimilaritySearchService().FindSimilarToSelectedImage(snapshot, 2));
    }

    [Fact]
    public void MultiRegionFallbackAddsCropCandidateOutsideBaselineRadii()
    {
        SearchIndexSnapshot baseline = CreateSnapshot(
            new(1, 0, 0, 100, 100, 1000),
            new(2, ulong.MaxValue, ulong.MaxValue, 75, 75, 750));
        byte[] hashes = new byte[2 * 72];
        hashes.AsSpan(72).Fill(0xFF);
        hashes.AsSpan(3 * 8, 8).Fill(0x5A);
        hashes.AsSpan(72 + 5 * 8, 8).Fill(0x5A);
        MultiRegionSearchIndexSnapshot regional = new(7, "test-profile", [1L, 2L], hashes);

        SimilaritySearchResult result = new SimilaritySearchService().FindSimilarToSelectedImage(
            baseline,
            regional,
            1,
            new SimilaritySearchOptions(topN: 10, maxPerceptualDistance: 0, maxDifferenceDistance: 0),
            new MultiRegionSearchOptions(topN: 10, maximumRegionDistance: 0));

        SimilarityMatch match = Assert.Single(result.Matches);
        Assert.Equal(2, match.ImageId);
        Assert.Equal(0, match.MultiRegionBestDistance);
        Assert.True(match.MultiRegionMatchedRegions >= 1);
    }

    [Fact]
    public void MultiRegionSearchRejectsSnapshotsFromDifferentScans()
    {
        SearchIndexSnapshot baseline = CreateSnapshot(new SearchIndexEntry(1, 0, 0, 100, 100, 1000));
        MultiRegionSearchIndexSnapshot regional = new(8, "test-profile", [1L], new byte[72]);

        Assert.Throws<ArgumentException>(() => new SimilaritySearchService().FindSimilarToSelectedImage(
            baseline, regional, 1));
    }

    [Fact]
    public void ExternalImageSearchReturnsTheBestLibraryCandidateWithoutRequiringAQueryId()
    {
        SearchIndexSnapshot baseline = CreateSnapshot(
            new(1, 0, 0, 100, 100, 1000),
            new(2, ulong.MaxValue, ulong.MaxValue, 100, 100, 1000));
        MultiRegionSearchIndexSnapshot regional = new(7, "test-profile", [1L, 2L],
            new byte[2 * MultiRegionHash576.ByteLength]);
        ImageFingerprintSet query = CreateExternalFingerprints(0, 0, new byte[MultiRegionHash576.ByteLength]);

        SimilaritySearchResult result = new SimilaritySearchService().FindSimilarToExternalImage(
            baseline,
            regional,
            query,
            queryFileSize: 1000,
            new SimilaritySearchOptions(topN: 1, maxPerceptualDistance: 0, maxDifferenceDistance: 0),
            new MultiRegionSearchOptions(topN: 2, maximumRegionDistance: 0));

        SimilarityMatch match = Assert.Single(result.Matches);
        Assert.Equal(0, result.QueryImageId);
        Assert.Equal(1, match.ImageId);
        Assert.Equal(100d, match.ScorePercent);
    }

    [Fact]
    public void ExternalImageSearchCanFindACropThroughMultiRegionFallback()
    {
        SearchIndexSnapshot baseline = CreateSnapshot(
            new SearchIndexEntry(9, ulong.MaxValue, ulong.MaxValue, 200, 100, 5000));
        byte[] indexedHash = Enumerable.Repeat((byte)0xFF, MultiRegionHash576.ByteLength).ToArray();
        indexedHash.AsSpan(4 * 8, 8).Fill(0x5A);
        MultiRegionSearchIndexSnapshot regional = new(7, "test-profile", [9L], indexedHash);
        byte[] queryHash = new byte[MultiRegionHash576.ByteLength];
        queryHash.AsSpan(2 * 8, 8).Fill(0x5A);
        ImageFingerprintSet query = CreateExternalFingerprints(0, 0, queryHash);

        SimilaritySearchResult result = new SimilaritySearchService().FindSimilarToExternalImage(
            baseline,
            regional,
            query,
            queryFileSize: 2000,
            new SimilaritySearchOptions(topN: 3, maxPerceptualDistance: 0, maxDifferenceDistance: 0),
            new MultiRegionSearchOptions(topN: 3, maximumRegionDistance: 0));

        SimilarityMatch match = Assert.Single(result.Matches);
        Assert.Equal(9, match.ImageId);
        Assert.Equal(0, match.MultiRegionBestDistance);
        Assert.True(match.MultiRegionMatchedRegions >= 1);
    }

    private static ImageFingerprintSet CreateExternalFingerprints(
        ulong perceptual,
        ulong difference,
        byte[] multiRegion)
    {
        return new ImageFingerprintSet(100, 100,
        [
            new Fingerprint(new FingerprintDescriptor("phash", 1, "test", 64, 32, 32),
                FingerprintValue.FromUInt64(perceptual)),
            new Fingerprint(new FingerprintDescriptor("dhash", 1, "test", 64, 9, 8),
                FingerprintValue.FromUInt64(difference)),
            new Fingerprint(new MultiRegionHash576().Descriptor,
                FingerprintValue.FromBytes(multiRegion, MultiRegionHash576.RegionCount * 64))
        ]);
    }

    private static SearchIndexSnapshot CreateSnapshot(params SearchIndexEntry[] entries) =>
        new(7, "test-profile", entries);
}
