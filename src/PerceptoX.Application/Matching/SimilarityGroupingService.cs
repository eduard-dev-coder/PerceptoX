using System.Numerics;

namespace PerceptoX.Application.Matching;

public sealed record SimilarityGroupMember(long ImageId, double ScorePercent)
{
    public bool BinaryIdentical { get; init; }
    public double? VisualDifference { get; init; }
}
public sealed record SimilarityGroup(long RepresentativeId, IReadOnlyList<SimilarityGroupMember> Members);
public readonly record struct GroupingVerification(bool Accepted, bool BinaryIdentical, double VisualDifference);

/// <summary>Direct-anchor grouping. Scores describe fingerprints, not binary file identity.</summary>
public static class SimilarityGroupingService
{
    public static IReadOnlyList<SimilarityGroup> Build(SearchIndexSnapshot snapshot,
        double minimumScore = 97, long maximumCandidateVisits = 128_000_000,
        Func<SearchIndexEntry, SearchIndexEntry, GroupingVerification>? verify = null,
        IProgress<int>? progress = null, int maximumImages = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!double.IsFinite(minimumScore) || minimumScore < 97 || minimumScore > 100)
            throw new ArgumentOutOfRangeException(nameof(minimumScore));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateVisits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImages);
        if (snapshot.Count > maximumImages)
            throw new InvalidOperationException("Similarity analysis exceeded its image budget; reduce the scope.");
        cancellationToken.ThrowIfCancellationRequested();
        SearchIndexEntry[] entries = snapshot.Entries.ToArray();
        Array.Sort(entries, static (a, b) =>
        {
            int area = ((long)b.Width * b.Height).CompareTo((long)a.Width * a.Height);
            if (area != 0) return area;
            int size = b.FileSize.CompareTo(a.FileSize);
            return size != 0 ? size : a.ImageId.CompareTo(b.ImageId);
        });
        // Without an image verifier: exact baseline p+d total <=3 at >=97.
        // With verification: candidates with p<=3 OR d<=3, then verified RGB score.
        // Either radius-three metric guarantees an equal 16-bit segment by pigeonhole.
        // This catches recompression where low-energy pHash bits are unstable but dHash is stable.
        int segments = verify is null ? 4 : 8;
        int[][] offsets = Enumerable.Range(0, segments).Select(_ => new int[65537]).ToArray();
        int[][] postings = Enumerable.Range(0, segments).Select(_ => new int[entries.Length]).ToArray();
        for (int i = 0; i < entries.Length; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int segment = 0; segment < segments; segment++)
            {
                int value = SegmentValue(entries[i], segment);
                offsets[segment][value + 1]++;
            }
        }
        for (int segment = 0; segment < segments; segment++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int value = 1; value <= 65536; value++) offsets[segment][value] += offsets[segment][value - 1];
            int[] cursor = (int[])offsets[segment].Clone();
            for (int i = 0; i < entries.Length; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                int value = SegmentValue(entries[i], segment);
                postings[segment][cursor[value]++] = i;
            }
        }
        bool[] assigned = new bool[entries.Length];
        int[] seen = new int[entries.Length];
        List<SimilarityGroup> groups = [];
        long visits = 0;
        for (int anchor = 0; anchor < entries.Length; anchor++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((anchor & 255) == 0) progress?.Report(anchor);
            if (assigned[anchor]) continue;
            SearchIndexEntry representative = entries[anchor];
            // Most images are singletons. Allocate a member list only after a verified match.
            List<SimilarityGroupMember>? members = null;
            int generation = anchor + 1;
            for (int segment = 0; segment < segments; segment++)
            {
                int value = SegmentValue(representative, segment);
                // Posting indices are sorted. Skip the entire already-visited prefix in O(log bucket).
                int first = FirstAfter(postings[segment], offsets[segment][value], offsets[segment][value + 1], anchor);
                for (int position = first; position < offsets[segment][value + 1]; position++)
                {
                    int candidate = postings[segment][position];
                    if (++visits > maximumCandidateVisits)
                        throw new InvalidOperationException("Similarity analysis exceeded its candidate budget; no complete result was published.");
                    if ((visits & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (candidate <= anchor || assigned[candidate] || seen[candidate] == generation) continue;
                    seen[candidate] = generation;
                    SearchIndexEntry item = entries[candidate];
                    int pDistance = BitOperations.PopCount(representative.PerceptualHash ^ item.PerceptualHash);
                    int dDistance = BitOperations.PopCount(representative.DifferenceHash ^ item.DifferenceHash);
                    int distance = pDistance + dDistance;
                    double score = 100 * (1 - distance / 128d);
                    if (verify is null ? score < minimumScore : pDistance > 3 && dDistance > 3) continue;
                    GroupingVerification evidence = verify?.Invoke(representative, item) ?? new(true, false, 0);
                    if (!evidence.Accepted) continue;
                    if (verify is not null)
                    {
                        score = evidence.BinaryIdentical ? 100 : 100 * (1 - evidence.VisualDifference);
                        if (!double.IsFinite(score) || score < minimumScore || score > 100) continue;
                    }
                    assigned[candidate] = true;
                    members ??= [new(representative.ImageId, 100)];
                    members.Add(new(item.ImageId, score) { BinaryIdentical = evidence.BinaryIdentical,
                        VisualDifference = verify is null ? null : evidence.VisualDifference });
                }
            }
            assigned[anchor] = true;
            if (members is not null)
            {
                members.Sort(static (a, b) =>
                {
                    int score = b.ScorePercent.CompareTo(a.ScorePercent);
                    return score != 0 ? score : a.ImageId.CompareTo(b.ImageId);
                });
                groups.Add(new(representative.ImageId, members.ToArray()));
            }
        }
        progress?.Report(entries.Length);
        return groups;
    }

    private static int SegmentValue(SearchIndexEntry entry, int segment)
        => (ushort)((segment < 4 ? entry.PerceptualHash : entry.DifferenceHash) >> ((segment % 4) * 16));

    private static int FirstAfter(int[] posting, int low, int high, int anchor)
    {
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (posting[middle] <= anchor) low = middle + 1; else high = middle;
        }
        return low;
    }
}
