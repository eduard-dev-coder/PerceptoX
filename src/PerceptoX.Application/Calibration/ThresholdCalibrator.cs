using System.Security.Cryptography;
using System.Text;

namespace PerceptoX.Application.Calibration;

/// <summary>Pure, bounded proposal generation. Does not activate profiles or access files.</summary>
public static class ThresholdCalibrator
{
    public static CalibrationProposal Propose(IEnumerable<FeedbackSample> samples,
        ThresholdProfile baseline, string candidateId, DateTimeOffset createdAtUtc,
        CalibrationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(baseline);
        baseline.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        if (candidateId == baseline.Id) throw new ArgumentException("A proposal needs a new profile ID.");
        options ??= new();
        options.Validate();
        FeedbackSample[] data = samples.ToArray();
        HashSet<(string Source, string Query, string Candidate)> pairs = [];
        Dictionary<string, string> querySources = new(StringComparer.Ordinal);
        foreach (FeedbackSample sample in data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(sample);
            sample.Validate();
            if (!pairs.Add((sample.SourceKey, sample.QueryKey, sample.CandidateKey)))
                throw new ArgumentException("Duplicate feedback pairs must be replaced before calibration.");
            if (querySources.TryGetValue(sample.QueryKey, out string? source) && source != sample.SourceKey)
                throw new ArgumentException("One query must identify one original source.");
            querySources[sample.QueryKey] = sample.SourceKey;
            if (sample.MultiRegionBestDistance is not null &&
                sample.RegionMeasurementDistance != baseline.MaximumRegionDistance)
                throw new ArgumentException("Regional observations were measured using another threshold.");
        }

        // Group original sources (and therefore all their query variants) together.
        // SHA256 ordering avoids dependence on input order and randomized string.GetHashCode.
        string[] sources = data.Select(sample => sample.SourceKey).Distinct(StringComparer.Ordinal)
            .OrderBy(key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))),
                StringComparer.Ordinal).ThenBy(key => key, StringComparer.Ordinal).ToArray();
        int holdoutSources = sources.Length < 2 ? sources.Length : Math.Max(1, (sources.Length + 3) / 4);
        HashSet<string> holdoutSourceKeys = sources.Take(holdoutSources).ToHashSet(StringComparer.Ordinal);
        FeedbackSample[] training = data.Where(sample => !holdoutSourceKeys.Contains(sample.SourceKey)).ToArray();
        FeedbackSample[] holdout = data.Where(sample => holdoutSourceKeys.Contains(sample.SourceKey)).ToArray();
        string[] trainingKeys = QueryKeys(training);
        string[] holdoutKeys = QueryKeys(holdout);
        List<string> warnings =
        [
            "Metrics describe labeled holdout pairs only; they are not match probabilities or whole-library recall.",
            "Regional distance is fixed at its measurement threshold; only minimum matched regions can be tuned.",
            "Reusing this holdout repeatedly for manual tuning can bias evaluation; use fresh independent labels."
        ];
        CalibrationEvaluation baselineHoldout = Evaluate(holdout, baseline);
        if (!HasSupport(training, trainingKeys.Length, options.MinimumTrainingSamples,
                options.MinimumTrainingQueries, options) ||
            !HasSupport(holdout, holdoutKeys.Length, options.MinimumHoldoutSamples,
                options.MinimumHoldoutQueries, options))
        {
            warnings.Add("Insufficient data: each partition requires its sample/query minimum, at least 20 positives and 20 negatives, and a class ratio no greater than 10:1.");
            return Result(CalibrationStatus.InsufficientData, null, null);
        }

        ThresholdProfile best = baseline;
        CalibrationEvaluation bestTraining = Evaluate(training, baseline);
        double bestObjective = Objective(bestTraining);
        bool tuneRegions = HasRegionalSupport(training);
        if (!tuneRegions)
            warnings.Add("Insufficient regional labels: minimum matched regions remains at the baseline value.");
        int adjustment = options.MaximumThresholdAdjustment;
        for (int p = Math.Max(0, baseline.MaxPerceptualDistance - adjustment);
             p <= Math.Min(64, baseline.MaxPerceptualDistance + adjustment); p++)
        for (int d = Math.Max(0, baseline.MaxDifferenceDistance - adjustment);
             d <= Math.Min(64, baseline.MaxDifferenceDistance + adjustment); d++)
        for (int regions = tuneRegions ? 1 : baseline.MinimumMatchedRegions;
             regions <= (tuneRegions ? 9 : baseline.MinimumMatchedRegions); regions++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThresholdProfile candidate = baseline with
            {
                MaxPerceptualDistance = p, MaxDifferenceDistance = d, MinimumMatchedRegions = regions
            };
            CalibrationEvaluation evaluation = Evaluate(training, candidate);
            double objective = Objective(evaluation);
            // Select only with training data. Stable ties prefer fewer false positives, then
            // smaller p/d thresholds and more regional evidence.
            if (objective > bestObjective || objective == bestObjective &&
                Prefer(candidate, evaluation, best, bestTraining))
            {
                best = candidate;
                bestTraining = evaluation;
                bestObjective = objective;
            }
        }
        ThresholdProfile proposed = best with
        {
            Id = candidateId, Version = checked(baseline.Version + 1), CreatedAtUtc = createdAtUtc
        };
        proposed.Validate();
        CalibrationEvaluation proposedHoldout = Evaluate(holdout, proposed);
        bool accepted = proposedHoldout.Precision is double precision &&
            precision >= (baselineHoldout.Precision ?? 0) &&
            proposedHoldout.Recall >= baselineHoldout.Recall &&
            proposedHoldout.FalsePositives <= baselineHoldout.FalsePositives &&
            (proposed.MinimumMatchedRegions == baseline.MinimumMatchedRegions || HasRegionalSupport(holdout));
        if (!accepted) warnings.Add("Holdout regression or undefined precision: proposal cannot be activated.");
        return Result(accepted ? CalibrationStatus.Validated : CalibrationStatus.Rejected,
            proposed, proposedHoldout);

        CalibrationProposal Result(CalibrationStatus status, ThresholdProfile? proposed,
            CalibrationEvaluation? evaluation) => new(status, baseline, proposed, baselineHoldout,
                evaluation, Array.AsReadOnly(trainingKeys), Array.AsReadOnly(holdoutKeys),
                Array.AsReadOnly(warnings.ToArray()));
    }

    private static string[] QueryKeys(FeedbackSample[] data) => data.Select(sample => sample.QueryKey)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static bool HasSupport(FeedbackSample[] data, int queries, int minimumSamples,
        int minimumQueries, CalibrationOptions options)
    {
        int positive = data.Count(sample => sample.IsSimilar);
        int negative = data.Length - positive;
        return data.Length >= minimumSamples && queries >= minimumQueries &&
            positive >= options.MinimumPositivesPerPartition &&
            negative >= options.MinimumNegativesPerPartition &&
            Math.Max(positive, negative) <= (long)Math.Min(positive, negative) * options.MaximumClassRatio;
    }

    private static bool HasRegionalSupport(FeedbackSample[] data) =>
        data.Count(sample => sample.MultiRegionBestDistance is not null && sample.IsSimilar) >= 20 &&
        data.Count(sample => sample.MultiRegionBestDistance is not null && !sample.IsSimilar) >= 20;

    private static CalibrationEvaluation Evaluate(FeedbackSample[] samples, ThresholdProfile profile)
    {
        int tp = 0, fp = 0, tn = 0, fn = 0;
        foreach (FeedbackSample sample in samples)
        {
            bool match = sample.PerceptualDistance <= profile.MaxPerceptualDistance &&
                sample.DifferenceDistance <= profile.MaxDifferenceDistance ||
                sample.MultiRegionBestDistance is int regional && regional <= profile.MaximumRegionDistance &&
                sample.MultiRegionMatchedRegions >= profile.MinimumMatchedRegions;
            if (sample.IsSimilar) { if (match) tp++; else fn++; }
            else { if (match) fp++; else tn++; }
        }
        return new(samples.Length, QueryKeys(samples).Length, tp, fp, tn, fn);
    }

    // F0.5 weights precision more than recall; this is an optimization score, not a probability.
    private static double Objective(CalibrationEvaluation evaluation) =>
        evaluation.TruePositives == 0 ? 0 : 1.25 * evaluation.TruePositives /
            (1.25 * evaluation.TruePositives + evaluation.FalsePositives + .25 * evaluation.FalseNegatives);

    private static bool Prefer(ThresholdProfile candidate, CalibrationEvaluation evaluation,
        ThresholdProfile current, CalibrationEvaluation currentEvaluation) =>
        (evaluation.FalsePositives, candidate.MaxPerceptualDistance + candidate.MaxDifferenceDistance,
            candidate.MaxPerceptualDistance, -candidate.MinimumMatchedRegions).CompareTo(
            (currentEvaluation.FalsePositives, current.MaxPerceptualDistance + current.MaxDifferenceDistance,
                current.MaxPerceptualDistance, -current.MinimumMatchedRegions)) < 0;
}
