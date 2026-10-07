using PerceptoX.Application.Matching;

namespace PerceptoX.Application.Tests;

public sealed class SimilarityGroupingTests
{
    private static SearchIndexEntry Entry(long id, ulong p, int width = 100, long size = 100)
        => new(id, p, 0, width, 100, size);
    private static SearchIndexSnapshot Snapshot(params SearchIndexEntry[] entries) => new(1, "test", entries);

    [Fact]
    public void PicksResolutionThenSizeWithoutPreferredFolder()
    {
        var groups = SimilarityGroupingService.Build(Snapshot(Entry(1, 0), Entry(2, 0, 200, 200), Entry(3, 0, 200, 300)));
        Assert.Equal(3, Assert.Single(groups).RepresentativeId);
        Assert.Equal(3, groups[0].Members.Count);
    }

    [Fact]
    public void DoesNotJoinThroughTransitiveBridge()
    {
        var groups = SimilarityGroupingService.Build(Snapshot(Entry(1, 0), Entry(2, 7), Entry(3, 63)));
        Assert.Equal(new long[] { 1, 2 }, Assert.Single(groups).Members.Select(m => m.ImageId));
    }

    [Fact]
    public void FindsThreeBitDifferencesAcrossSegments()
    {
        ulong changed = 1UL | (1UL << 16) | (1UL << 32);
        Assert.Single(SimilarityGroupingService.Build(Snapshot(Entry(1, 0), Entry(2, changed))));
    }

    [Fact]
    public void BudgetFailureDoesNotReturnTruncatedGroups()
        => Assert.Throws<InvalidOperationException>(() => SimilarityGroupingService.Build(
            Snapshot(Entry(1, 0), Entry(2, 0)), maximumCandidateVisits: 1));

    [Fact]
    public void CancellationIsObservedEvenForEmptySnapshot()
        => Assert.Throws<OperationCanceledException>(() => SimilarityGroupingService.Build(
            Snapshot(), cancellationToken: new CancellationToken(true)));

    [Fact]
    public void HundredRequiresBothFingerprintsEqual()
    {
        var snapshot = Snapshot(Entry(1, 0), Entry(2, 0) with { DifferenceHash = 1 });
        Assert.Empty(SimilarityGroupingService.Build(snapshot, 100));
    }

    [Fact]
    public void RejectedVerificationDoesNotConsumeMemberFromAnotherGroup()
    {
        var groups = SimilarityGroupingService.Build(Snapshot(Entry(1, 0), Entry(2, 0), Entry(3, 0)),
            verify: (anchor, _) => new(anchor.ImageId != 1, false, 0));
        Assert.Equal(2, Assert.Single(groups).RepresentativeId);
        Assert.Equal(new long[] { 2, 3 }, groups[0].Members.Select(m => m.ImageId));
    }

    [Fact]
    public void ImageBudgetIsCheckedBeforeAllocatingCandidateIndex()
        => Assert.Throws<InvalidOperationException>(() => SimilarityGroupingService.Build(
            Snapshot(Entry(1, 0), Entry(2, 0)), maximumImages: 1));

    [Fact]
    public void VerifiedSearchRecoversStableDHashWhenPerceptualHashIsUnstable()
    {
        var groups = SimilarityGroupingService.Build(Snapshot(Entry(1, 0), Entry(2, ulong.MaxValue)),
            verify: static (_, _) => new(true, false, 0.01));
        Assert.Equal(99, Assert.Single(groups).Members.Single(m => m.ImageId == 2).ScorePercent);
    }

    [Fact]
    public void VerifiedScoreRejectsRgbDifferenceBelowThreshold()
        => Assert.Empty(SimilarityGroupingService.Build(Snapshot(Entry(1, 0), Entry(2, 0)),
            verify: static (_, _) => new(true, false, 0.04)));

    [Fact]
    public void GroupMembersAreNotLimitedBySearchTopN()
    {
        var snapshot = Snapshot(Enumerable.Range(1, 150).Select(i => Entry(i, 0)).ToArray());
        Assert.Equal(150, Assert.Single(SimilarityGroupingService.Build(snapshot)).Members.Count);
    }

    [Fact]
    public void SegmentedCandidatesMatchExhaustiveDirectAnchorBaseline()
    {
        Random random = new(719);
        List<SearchIndexEntry> entries = [];
        for (int family = 0; family < 40; family++)
        {
            ulong basis = (ulong)random.NextInt64();
            for (int variant = 0; variant < 5; variant++)
            {
                ulong hash = basis;
                for (int bit = 0; bit < variant; bit++) hash ^= 1UL << random.Next(64);
                entries.Add(Entry(entries.Count + 1, hash));
            }
        }
        bool[] assigned = new bool[entries.Count];
        List<long[]> expected = [];
        for (int anchor = 0; anchor < entries.Count; anchor++)
        {
            if (assigned[anchor]) continue;
            List<long> members = [entries[anchor].ImageId];
            for (int other = anchor + 1; other < entries.Count; other++)
            {
                if (assigned[other] || System.Numerics.BitOperations.PopCount(
                    entries[anchor].PerceptualHash ^ entries[other].PerceptualHash) > 3) continue;
                assigned[other] = true;
                members.Add(entries[other].ImageId);
            }
            assigned[anchor] = true;
            if (members.Count > 1) expected.Add(members.Order().ToArray());
        }
        var actual = SimilarityGroupingService.Build(Snapshot(entries.ToArray()));
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], actual[i].Members.Select(m => m.ImageId).Order());
    }
}
