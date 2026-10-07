using System.Globalization;
using PerceptoX.Application.Indexing;
using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;
using PerceptoX.Infrastructure.Exporting;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Indexing;
using PerceptoX.Infrastructure.Persistence;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    try
    {
        if (arguments.Length == 3 && arguments[0].Equals("compare", StringComparison.OrdinalIgnoreCase))
        {
            ImageComparisonService service = new(new ImageSharpFingerprintExtractor());
            ImageComparisonResult result = service.Compare(arguments[1], arguments[2]);
            Console.WriteLine($"First:  {result.Left.Width}x{result.Left.Height}");
            Console.WriteLine($"        pHash {result.Left.GetRequired("phash").Value}");
            Console.WriteLine($"        dHash {result.Left.GetRequired("dhash").Value}");
            Console.WriteLine($"Second: {result.Right.Width}x{result.Right.Height}");
            Console.WriteLine($"        pHash {result.Right.GetRequired("phash").Value}");
            Console.WriteLine($"        dHash {result.Right.GetRequired("dhash").Value}");
            Console.WriteLine($"Hamming: pHash {result.PerceptualDistance.DifferingBits}/64, " +
                $"dHash {result.DifferenceDistance.DifferingBits}/64");
            MultiRegionHashMatch regional = MultiRegionHashComparer.Compare(
                result.Left.GetRequired("multiregion"), result.Right.GetRequired("multiregion"), 1);
            Console.WriteLine($"Multi-region: best={regional.BestDistance}/64 " +
                $"matched-regions={regional.MatchedRegions}/9 (experimental)");
            Console.WriteLine($"Baseline similarity: {result.BaselineSimilarityPercent:F2}% (uncalibrated)");
            return 0;
        }

        if (arguments.Length == 4 && arguments[0].Equals("index", StringComparison.OrdinalIgnoreCase))
        {
            using ImageSharpImageProcessor processor = new();
            IndexingRequest request = new(arguments[1], arguments[2], arguments[3],
                processor.ProcessingProfileId);
            using CancellationTokenSource cancellation = new();
            ConsoleCancelEventHandler handler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += handler;
            try
            {
                IndexingResult result = await new ImageIndexer(processor).IndexAsync(request, cancellation.Token);
                Console.WriteLine($"Scan {result.ScanId}: found={result.Discovered}, " +
                    $"unchanged={result.SkippedUnchanged}, processed={result.Processed}, " +
                    $"failed={result.Failed}, deactivated={result.Deactivated}");
                return result.Failed == 0 ? 0 : 3;
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }

        if (arguments.Length == 3 && arguments[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            SqliteIndexReader reader = new(arguments[2]);
            Console.WriteLine($"Active images: {reader.CountActive(arguments[1])}");
            using ImageSharpImageProcessor processor = new();
            var snapshot = reader.LoadSnapshot(arguments[1], processor.ProcessingProfileId);
            MultiRegionSearchIndexSnapshot regional = reader.LoadMultiRegionSnapshot(
                arguments[1], processor.ProcessingProfileId);
            Console.WriteLine($"Search snapshot: scan={snapshot.ScanId}, entries={snapshot.Count}");
            Console.WriteLine($"Multi-region snapshot: scan={regional.ScanId}, entries={regional.Count}");
            return 0;
        }

        if (arguments.Length is 4 or 5 &&
            arguments[0].Equals("find-similar", StringComparison.OrdinalIgnoreCase))
        {
            int topN = ParseTopN(arguments.Length == 5 ? arguments[4] : null);
            (SqliteIndexReader reader, SearchIndexSnapshot snapshot,
                MultiRegionSearchIndexSnapshot regional, long queryId) =
                LoadSearch(arguments[1], arguments[2], arguments[3]);
            SimilaritySearchResult result = new SimilaritySearchService().FindSimilarToSelectedImage(
                snapshot, regional, queryId, new SimilaritySearchOptions(topN));
            SearchReport report = SearchReportFactory.Create(reader, result);
            foreach (SearchReportEntry entry in report.Entries.Where(entry => !entry.IsQuery))
            {
                string regionalText = entry.MultiRegionBestDistance is int regionalDistance
                    ? $" mr={regionalDistance}/64 regions={entry.MultiRegionMatchedRegions}/9"
                    : string.Empty;
                Console.WriteLine($"{entry.ScorePercent.ToString("F2", CultureInfo.InvariantCulture)}% " +
                    $"p={entry.PerceptualDistance}/64 d={entry.DifferenceDistance}/64{regionalText} " +
                    entry.Detail.Image.FilePath);
            }

            Console.WriteLine("Scores are uncalibrated and are not duplicate probabilities.");
            return 0;
        }

        if (arguments.Length is 7 or 8 &&
            arguments[0].Equals("export", StringComparison.OrdinalIgnoreCase))
        {
            int topN = ParseTopN(arguments.Length == 8 ? arguments[7] : null);
            (SqliteIndexReader reader, SearchIndexSnapshot snapshot,
                MultiRegionSearchIndexSnapshot regional, long queryId) =
                LoadSearch(arguments[1], arguments[2], arguments[4]);
            SimilaritySearchResult result = new SimilaritySearchService().FindSimilarToSelectedImage(
                snapshot, regional, queryId, new SimilaritySearchOptions(topN));
            SearchReport report = SearchReportFactory.Create(reader, result);
            SearchReportExporter.ExportCsv(report, arguments[5]);
            SearchReportExporter.ExportHtml(report, arguments[3], arguments[6]);
            Console.WriteLine($"Exported {report.Entries.Count - 1} uncalibrated matches.");
            return 0;
        }

        if (arguments.Length is 3 or 4 && arguments[0].Equals("groups", StringComparison.OrdinalIgnoreCase))
        {
            int maximumGroups = ParseBounded(arguments.Length == 4 ? arguments[3] : null, 100, 1, 10_000,
                "maximum groups");
            using ImageSharpImageProcessor processor = new();
            SearchIndexSnapshot snapshot = new SqliteIndexReader(arguments[2]).LoadSnapshot(
                arguments[1], processor.ProcessingProfileId);
            IReadOnlyList<FingerprintCandidateGroup> groups = FingerprintCandidateGroupService.BuildPreview(
                snapshot, maximumGroups);
            foreach (FingerprintCandidateGroup group in groups)
            {
                Console.WriteLine($"candidate-group representative={group.RepresentativeImageId} " +
                    $"members={group.TotalMembers} preview={string.Join(',', group.PreviewImageIds)}");
            }

            Console.WriteLine("Groups are hash-equality candidates, not verified byte-identical duplicates.");
            return 0;
        }

        if (arguments.Length is >= 4 and <= 9 &&
            arguments[0].Equals("evaluate-synthetic", StringComparison.OrdinalIgnoreCase))
        {
            int topN = ParseTopN(arguments.Length >= 5 ? arguments[4] : null);
            int maxPerceptualDistance = ParseBounded(arguments.Length >= 6 ? arguments[5] : null,
                2, 0, 64, "maximum pHash distance");
            int maxDifferenceDistance = ParseBounded(arguments.Length >= 7 ? arguments[6] : null,
                2, 0, 64, "maximum dHash distance");
            int maxMultiRegionDistance = ParseBounded(arguments.Length >= 8 ? arguments[7] : null,
                1, 0, 64, "maximum multi-region distance");
            int minimumMatchedRegions = ParseBounded(arguments.Length == 9 ? arguments[8] : null,
                1, 1, 9, "minimum matched regions");
            using ImageSharpImageProcessor processor = new();
            SqliteIndexReader reader = new(arguments[2]);
            SearchIndexSnapshot snapshot = reader.LoadSnapshot(arguments[1], processor.ProcessingProfileId);
            MultiRegionSearchIndexSnapshot regional = reader.LoadMultiRegionSnapshot(
                arguments[1], processor.ProcessingProfileId);
            string manifest = Path.GetFullPath(arguments[3]);
            string manifestRoot = Path.GetDirectoryName(manifest)!;
            var rows = File.ReadLines(manifest).Skip(1).Select(ParseManifestRow).ToArray();
            string[] paths = rows.Select(row => Path.GetFullPath(Path.Combine(manifestRoot,
                row.RelativePath.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
            IReadOnlyDictionary<string, long> ids = reader.ResolveActiveImageIds(arguments[1], paths);
            LabeledImage[] labels = rows.Select((row, index) =>
            {
                if (!ids.TryGetValue(paths[index], out long imageId))
                {
                    throw new InvalidDataException($"Manifest image is not active in the index: {row.RelativePath}");
                }

                return new LabeledImage(imageId, row.GroupId, row.Variant);
            }).ToArray();
            MatchingEvaluation evaluation = new LabeledDatasetEvaluator().Evaluate(
                snapshot,
                regional,
                labels,
                "original",
                new SimilaritySearchOptions(topN, maxPerceptualDistance, maxDifferenceDistance),
                new MultiRegionSearchOptions(
                    Math.Min(topN * 4, 4000), maxMultiRegionDistance, minimumMatchedRegions));
            Console.WriteLine($"queries={evaluation.Queries} returned={evaluation.Returned} " +
                $"relevant={evaluation.RelevantReturned}/{evaluation.RelevantExpected} " +
                $"precision={evaluation.Precision.ToString("P2", CultureInfo.InvariantCulture)} " +
                $"recall={evaluation.Recall.ToString("P2", CultureInfo.InvariantCulture)}");
            foreach (VariantRecall variant in evaluation.RecallByVariant)
            {
                Console.WriteLine($"{variant.Variant}: {variant.Found}/{variant.Expected} " +
                    $"({variant.Recall.ToString("P2", CultureInfo.InvariantCulture)})");
            }

            return 0;
        }

        Console.Error.WriteLine("Usage: perceptox compare <first-image> <second-image>");
        Console.Error.WriteLine("       perceptox index <library-root> <database-file> <thumbnail-cache-root>");
        Console.Error.WriteLine("       perceptox status <library-root> <database-file>");
        Console.Error.WriteLine("       perceptox find-similar <library-root> <database-file> <image-path> [top-n]");
        Console.Error.WriteLine("       perceptox export <library-root> <database-file> <thumbnail-cache> <image-path> <new-csv> <new-html-directory> [top-n]");
        Console.Error.WriteLine("       perceptox groups <library-root> <database-file> [maximum-groups]");
        Console.Error.WriteLine("       perceptox evaluate-synthetic <library-root> <database-file> <manifest.csv> [top-n] [max-phash-distance] [max-dhash-distance] [max-multiregion-distance] [min-matched-regions]");
        return 2;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Indexing cancelled; the previous active index was preserved.");
        return 130;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"PerceptoX error: {exception.Message}");
        return 1;
    }
}

static (SqliteIndexReader Reader, SearchIndexSnapshot Snapshot,
    MultiRegionSearchIndexSnapshot Regional, long QueryId) LoadSearch(
    string libraryRoot, string databasePath, string imagePath)
{
    SqliteIndexReader reader = new(databasePath);
    var image = reader.FindByPath(libraryRoot, imagePath);
    if (image is null || !image.IsActive)
    {
        throw new KeyNotFoundException("The selected image is not active in this library index.");
    }

    using ImageSharpImageProcessor processor = new();
    SearchIndexSnapshot snapshot = reader.LoadSnapshot(libraryRoot, processor.ProcessingProfileId);
    MultiRegionSearchIndexSnapshot regional = reader.LoadMultiRegionSnapshot(
        libraryRoot, processor.ProcessingProfileId);
    return (reader, snapshot, regional, image.Id);
}

static int ParseTopN(string? value) => ParseBounded(value, 50, 1, 1000, "top-n");

static int ParseBounded(string? value, int defaultValue, int minimum, int maximum, string name)
{
    if (value is null)
    {
        return defaultValue;
    }

    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
        parsed < minimum || parsed > maximum)
    {
        throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
    }

    return parsed;
}

static (string RelativePath, string GroupId, string Variant) ParseManifestRow(string line)
{
    string[] columns = line.Split(',');
    if (columns.Length != 3 || columns.Any(string.IsNullOrWhiteSpace))
    {
        throw new InvalidDataException("The synthetic manifest must contain relative_path,base_id,transform.");
    }

    return (columns[0], columns[1], columns[2]);
}
