namespace PerceptoX.Application.Matching;

public interface IHammingSearchIndex
{
    void VisitCandidates(SearchIndexSnapshot snapshot, SearchIndexEntry query,
        int maximumPerceptualDistance, Action<SearchIndexEntry, int> visitor,
        CancellationToken cancellationToken = default);
}
