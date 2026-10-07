using PerceptoX.Application.Matching;
using PerceptoX.Infrastructure.Persistence;

namespace PerceptoX.Infrastructure.Exporting;

public static class SearchReportFactory
{
    public static SearchReport Create(SqliteIndexReader reader, SimilaritySearchResult result)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(result);
        long[] ids = [result.QueryImageId, .. result.Matches.Select(match => match.ImageId)];
        IReadOnlyDictionary<long, SearchImageDetail> details = reader.LoadDetails(ids);
        if (!details.TryGetValue(result.QueryImageId, out SearchImageDetail? query))
        {
            throw new InvalidDataException("The selected image disappeared before report creation.");
        }

        List<SearchReportEntry> entries = [new(query, 0, 0, 100d, true)];
        foreach (SimilarityMatch match in result.Matches)
        {
            if (details.TryGetValue(match.ImageId, out SearchImageDetail? detail))
            {
                entries.Add(new SearchReportEntry(detail, match.PerceptualDistance,
                    match.DifferenceDistance, match.ScorePercent, false,
                    match.MultiRegionBestDistance, match.MultiRegionMatchedRegions));
            }
        }

        return new SearchReport(result.ScanId, result.ProcessingProfileId, entries);
    }
}
