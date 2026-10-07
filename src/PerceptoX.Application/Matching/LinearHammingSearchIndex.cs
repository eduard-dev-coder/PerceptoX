using System.Numerics;

namespace PerceptoX.Application.Matching;

public sealed class LinearHammingSearchIndex : IHammingSearchIndex
{
    public void VisitCandidates(SearchIndexSnapshot snapshot, SearchIndexEntry query,
        int maximumPerceptualDistance, Action<SearchIndexEntry, int> visitor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPerceptualDistance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPerceptualDistance, 64);

        ReadOnlySpan<SearchIndexEntry> entries = snapshot.Entries;
        for (int index = 0; index < entries.Length; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            SearchIndexEntry candidate = entries[index];
            int distance = BitOperations.PopCount(query.PerceptualHash ^ candidate.PerceptualHash);
            if (distance <= maximumPerceptualDistance)
            {
                visitor(candidate, distance);
            }
        }
    }
}
