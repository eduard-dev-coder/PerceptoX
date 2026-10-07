using PerceptoX.Application.Matching;

namespace PerceptoX.Application.Tests;

public sealed class FingerprintCandidateGroupServiceTests
{
    [Fact]
    public void GroupsOnlyMatchingHashPairsAndDoesNotClaimByteIdentity()
    {
        SearchIndexSnapshot snapshot = new(3, "test", [
            new(5, 10, 20, 100, 100, 1000),
            new(2, 10, 20, 50, 50, 200),
            new(8, 10, 21, 100, 100, 1000),
            new(1, 10, 20, 100, 100, 1000)
        ]);

        FingerprintCandidateGroup group = Assert.Single(
            FingerprintCandidateGroupService.BuildPreview(snapshot));

        Assert.Equal(1, group.RepresentativeImageId);
        Assert.Equal(3, group.TotalMembers);
        Assert.Equal([1L, 2L, 5L], group.PreviewImageIds);
        Assert.False(group.IsVerifiedExactDuplicate);
    }

    [Fact]
    public void PreviewBoundsMemberListWithoutLosingTotalCount()
    {
        SearchIndexEntry[] entries = Enumerable.Range(1, 1000)
            .Select(id => new SearchIndexEntry(id, 1, 2, 100, 100, 1000))
            .ToArray();
        SearchIndexSnapshot snapshot = new(3, "test", entries);

        FingerprintCandidateGroup group = Assert.Single(
            FingerprintCandidateGroupService.BuildPreview(snapshot, maximumMembersPerGroup: 10));

        Assert.Equal(1000, group.TotalMembers);
        Assert.Equal(10, group.PreviewImageIds.Count);
    }
}
