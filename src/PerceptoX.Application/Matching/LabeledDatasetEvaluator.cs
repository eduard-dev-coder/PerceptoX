namespace PerceptoX.Application.Matching;

public sealed record LabeledImage
{
    public LabeledImage(long imageId, string groupId, string variant)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        ImageId = imageId;
        GroupId = groupId;
        Variant = variant;
    }

    public long ImageId { get; }
    public string GroupId { get; }
    public string Variant { get; }
}

public sealed record VariantRecall(string Variant, int Found, int Expected)
{
    public double Recall => Expected == 0 ? 0d : (double)Found / Expected;
}

public sealed record MatchingEvaluation(
    int Queries,
    int Returned,
    int RelevantReturned,
    int RelevantExpected,
    IReadOnlyList<VariantRecall> RecallByVariant)
{
    public double Precision => Returned == 0 ? 0d : (double)RelevantReturned / Returned;
    public double Recall => RelevantExpected == 0 ? 0d : (double)RelevantReturned / RelevantExpected;
}

public sealed class LabeledDatasetEvaluator(SimilaritySearchService? searchService = null)
{
    private readonly SimilaritySearchService _searchService = searchService ?? new SimilaritySearchService();

    public MatchingEvaluation Evaluate(SearchIndexSnapshot snapshot, IEnumerable<LabeledImage> images,
        string queryVariant, SimilaritySearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return EvaluateCore(snapshot, images, queryVariant,
            imageId => _searchService.FindSimilarToSelectedImage(
                snapshot, imageId, options, cancellationToken), cancellationToken);
    }

    public MatchingEvaluation Evaluate(
        SearchIndexSnapshot snapshot,
        MultiRegionSearchIndexSnapshot multiRegionSnapshot,
        IEnumerable<LabeledImage> images,
        string queryVariant,
        SimilaritySearchOptions options,
        MultiRegionSearchOptions? multiRegionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(multiRegionSnapshot);
        ArgumentNullException.ThrowIfNull(options);
        return EvaluateCore(snapshot, images, queryVariant,
            imageId => _searchService.FindSimilarToSelectedImage(
                snapshot, multiRegionSnapshot, imageId, options, multiRegionOptions, cancellationToken),
            cancellationToken);
    }

    private static MatchingEvaluation EvaluateCore(
        SearchIndexSnapshot snapshot,
        IEnumerable<LabeledImage> images,
        string queryVariant,
        Func<long, SimilaritySearchResult> search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryVariant);
        LabeledImage[] labels = images.ToArray();
        Dictionary<long, LabeledImage> byId = labels.ToDictionary(item => item.ImageId);
        ILookup<string, LabeledImage> byGroup = labels.ToLookup(item => item.GroupId, StringComparer.Ordinal);
        Dictionary<string, (int Found, int Expected)> variants = new(StringComparer.Ordinal);
        int queries = 0;
        int returned = 0;
        int relevantReturned = 0;
        int relevantExpected = 0;
        foreach (LabeledImage query in labels.Where(item => item.Variant.Equals(queryVariant,
            StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LabeledImage[] expected = byGroup[query.GroupId].Where(item => item.ImageId != query.ImageId).ToArray();
            SimilaritySearchResult result = search(query.ImageId);
            HashSet<long> resultIds = result.Matches.Select(match => match.ImageId).ToHashSet();
            queries++;
            returned += result.Matches.Count;
            relevantExpected += expected.Length;
            foreach (LabeledImage target in expected)
            {
                bool found = resultIds.Contains(target.ImageId);
                (int foundCount, int expectedCount) = variants.GetValueOrDefault(target.Variant);
                variants[target.Variant] = (foundCount + (found ? 1 : 0), expectedCount + 1);
                if (found)
                {
                    relevantReturned++;
                }
            }

            foreach (SimilarityMatch match in result.Matches)
            {
                if (!byId.ContainsKey(match.ImageId))
                {
                    throw new InvalidDataException("The search returned an image absent from the labels.");
                }
            }
        }

        VariantRecall[] recallByVariant = variants.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new VariantRecall(item.Key, item.Value.Found, item.Value.Expected)).ToArray();
        return new MatchingEvaluation(queries, returned, relevantReturned, relevantExpected, recallByVariant);
    }
}
