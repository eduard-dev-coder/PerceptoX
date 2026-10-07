namespace PerceptoX.Application.Matching;

public static class FingerprintCandidateGroupService
{
    public static IReadOnlyList<FingerprintCandidateGroup> BuildPreview(SearchIndexSnapshot snapshot,
        int maximumGroups = 100, int maximumMembersPerGroup = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumGroups, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumGroups, 10_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMembersPerGroup, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumMembersPerGroup, 10_000);

        SearchIndexEntry[] sorted = snapshot.Entries.ToArray();
        Array.Sort(sorted, static (left, right) =>
        {
            int compare = left.PerceptualHash.CompareTo(right.PerceptualHash);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.DifferenceHash.CompareTo(right.DifferenceHash);
            return compare != 0 ? compare : left.ImageId.CompareTo(right.ImageId);
        });

        List<FingerprintCandidateGroup> groups = [];
        for (int first = 0; first < sorted.Length;)
        {
            if ((first & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int last = first + 1;
            while (last < sorted.Length &&
                sorted[last].PerceptualHash == sorted[first].PerceptualHash &&
                sorted[last].DifferenceHash == sorted[first].DifferenceHash)
            {
                last++;
            }

            int members = last - first;
            if (members > 1)
            {
                long[] preview = new long[Math.Min(members, maximumMembersPerGroup)];
                for (int index = 0; index < preview.Length; index++)
                {
                    preview[index] = sorted[first + index].ImageId;
                }

                groups.Add(new FingerprintCandidateGroup(sorted[first].ImageId, members, preview));
                if (groups.Count == maximumGroups)
                {
                    break;
                }
            }

            first = last;
        }

        return groups;
    }
}
