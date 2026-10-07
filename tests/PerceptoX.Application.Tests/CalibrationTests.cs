using PerceptoX.Application.Calibration;

namespace PerceptoX.Application.Tests;

public sealed class CalibrationTests
{
    private static readonly ThresholdProfile Baseline = ThresholdProfile.CreateDefault("test-profile");
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProposalReducesFalsePositivesAndReportsOnlyHoldoutMetrics()
    {
        CalibrationProposal result = Propose(Data());

        Assert.Equal(CalibrationStatus.Validated, result.Status);
        Assert.Equal(50, result.BaselineHoldout.Samples);
        Assert.Equal(25, result.BaselineHoldout.FalsePositives);
        Assert.Equal(.5, result.BaselineHoldout.Precision);
        Assert.Equal(1, result.BaselineHoldout.Recall);
        CalibrationEvaluation evaluation = Assert.IsType<CalibrationEvaluation>(result.ProposedHoldout);
        Assert.Equal(25, evaluation.TruePositives);
        Assert.Equal(0, evaluation.FalsePositives);
        Assert.Equal(25, evaluation.TrueNegatives);
        Assert.Equal(0, evaluation.FalseNegatives);
        Assert.Equal(1, evaluation.Precision);
        Assert.Equal(1, evaluation.Recall);
        Assert.Equal(Baseline.MaximumRegionDistance, result.ProposedProfile!.MaximumRegionDistance);
        Assert.Equal(Baseline.MinimumMatchedRegions, result.ProposedProfile.MinimumMatchedRegions);
        Assert.Equal(2, result.ProposedProfile.Version);
        Assert.Equal(Timestamp, result.ProposedProfile.CreatedAtUtc);
    }

    [Fact]
    public void HoldoutLabelsDoNotChooseThresholdsAndRegressionRejectsActivation()
    {
        FeedbackSample[] original = Data();
        CalibrationProposal first = Propose(original);
        HashSet<string> holdout = first.HoldoutQueryKeys.ToHashSet(StringComparer.Ordinal);
        FeedbackSample[] changed = original.Select(sample =>
            holdout.Contains(sample.QueryKey) && sample.IsSimilar
                ? sample with { PerceptualDistance = 2, DifferenceDistance = 2 }
                : sample).ToArray();

        CalibrationProposal second = Propose(changed);

        Assert.Equal(first.ProposedProfile, second.ProposedProfile);
        Assert.Equal(CalibrationStatus.Rejected, second.Status);
        Assert.Equal(1, second.BaselineHoldout.Recall);
        Assert.Equal(0, second.ProposedHoldout!.Recall);
        Assert.Throws<InvalidOperationException>(() => new ProfileSelection(Baseline).Apply(second));
    }

    [Fact]
    public void QueryVariantsFromOneOriginalRemainInTheSamePartition()
    {
        FeedbackSample[] samples = Data().Select(sample => sample with
        {
            QueryKey = sample.QueryKey + (sample.IsSimilar ? "-crop" : "-resize")
        }).ToArray();
        CalibrationProposal result = Propose(samples);
        Assert.Empty(result.TrainingQueryKeys.Intersect(result.HoldoutQueryKeys));
        HashSet<string> trainingQueries = result.TrainingQueryKeys.ToHashSet();
        HashSet<string> holdoutQueries = result.HoldoutQueryKeys.ToHashSet();
        string[] trainingSources = samples.Where(sample => trainingQueries.Contains(sample.QueryKey))
            .Select(sample => sample.SourceKey).Distinct().ToArray();
        string[] holdoutSources = samples.Where(sample => holdoutQueries.Contains(sample.QueryKey))
            .Select(sample => sample.SourceKey).Distinct().ToArray();
        Assert.Empty(trainingSources.Intersect(holdoutSources));
        Assert.Equal(15, trainingSources.Length);
        Assert.Equal(5, holdoutSources.Length);
    }

    [Fact]
    public void SmallDatasetCannotProduceOrApplyAValidatedProfile()
    {
        CalibrationProposal result = Propose(Data().Take(10));
        Assert.Equal(CalibrationStatus.InsufficientData, result.Status);
        Assert.Null(result.ProposedProfile);
        Assert.Null(result.ProposedHoldout);
        Assert.Contains(result.Warnings, warning => warning.Contains("Insufficient data", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => new ProfileSelection(Baseline).Apply(result));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThresholdCalibrator.Propose(
            Data(), Baseline, "new", Timestamp, new CalibrationOptions(MinimumTrainingSamples: 5)));
    }

    [Fact]
    public void ManyLabelsForFewQueriesRemainInsufficient()
    {
        FeedbackSample[] samples = Data().Select(sample => sample with
        {
            SourceKey = "one-source", QueryKey = "one-query",
            CandidateKey = sample.SourceKey + ":" + sample.CandidateKey
        }).ToArray();
        Assert.Equal(CalibrationStatus.InsufficientData, Propose(samples).Status);
    }

    [Fact]
    public void MissingNegativeLabelsAreNotValidationEvidence()
    {
        Assert.Equal(CalibrationStatus.InsufficientData,
            Propose(Data().Select(sample => sample with { IsSimilar = true })).Status);
    }

    [Fact]
    public void RegionalFalsePositivesRequireMoreMatchedRegions()
    {
        FeedbackSample[] samples = Data().Select(sample => sample with
        {
            PerceptualDistance = 20, DifferenceDistance = 20,
            MultiRegionBestDistance = 0, MultiRegionMatchedRegions = sample.IsSimilar ? 3 : 1
        }).ToArray();
        CalibrationProposal result = Propose(samples);
        Assert.Equal(CalibrationStatus.Validated, result.Status);
        Assert.Equal(3, result.ProposedProfile!.MinimumMatchedRegions);
        Assert.Equal(1, result.ProposedProfile.MaximumRegionDistance);
        Assert.Equal(0, result.ProposedHoldout!.FalsePositives);
        Assert.Equal(25, result.ProposedHoldout.TruePositives);
    }

    [Fact]
    public void RegionalCountCannotBeUsedAtAnotherDistanceThreshold()
    {
        FeedbackSample sample = Data()[0] with
        {
            MultiRegionBestDistance = 0, MultiRegionMatchedRegions = 2,
            RegionMeasurementDistance = 2
        };
        Assert.Throws<ArgumentException>(() => Propose([sample]));
    }

    [Fact]
    public void DuplicateOrAmbiguousQueriesAreRejected()
    {
        FeedbackSample sample = Data()[0];
        Assert.Throws<ArgumentException>(() => Propose([sample, sample]));
        Assert.Throws<ArgumentException>(() => Propose(
            [sample, sample with { SourceKey = "another-original", CandidateKey = "other-candidate" }]));
    }

    [Fact]
    public void ApplyRollbackResetPreserveProfilesAndRequireExplicitCalls()
    {
        ProfileSelection initial = new(Baseline);
        CalibrationProposal proposal = Propose(Data());
        Assert.Equal(Baseline, initial.Active);
        Assert.Null(initial.Previous);
        ProfileSelection applied = initial.Apply(proposal);
        Assert.Equal(proposal.ProposedProfile, applied.Active);
        Assert.Equal(Baseline, applied.Previous);
        ProfileSelection rolledBack = applied.Rollback();
        Assert.Equal(Baseline, rolledBack.Active);
        Assert.Equal(applied.Active, rolledBack.Previous);
        Assert.Equal(Baseline, applied.Reset(Baseline).Active);
        Assert.Equal(applied.Active, applied.Reset(Baseline).Previous);
        Assert.Equal(Baseline, initial.Active);
        Assert.Throws<InvalidOperationException>(() => initial.Rollback());
        Assert.Throws<InvalidOperationException>(() => applied.Apply(proposal));
        Assert.Throws<InvalidOperationException>(() => applied.Reset(
            ThresholdProfile.CreateDefault("another-processing-profile")));
    }

    [Fact]
    public void InputOrderingDoesNotChangePartitionsOrProposedThresholds()
    {
        CalibrationProposal forward = Propose(Data());
        CalibrationProposal reverse = Propose(Data().Reverse());
        Assert.Equal(forward.ProposedProfile, reverse.ProposedProfile);
        Assert.Equal(forward.HoldoutQueryKeys, reverse.HoldoutQueryKeys);
        Assert.Equal(forward.TrainingQueryKeys, reverse.TrainingQueryKeys);
        Assert.Equal(forward.ProposedHoldout, reverse.ProposedHoldout);
    }

    private static CalibrationProposal Propose(IEnumerable<FeedbackSample> samples) =>
        ThresholdCalibrator.Propose(samples, Baseline, "candidate-v2", Timestamp);

    private static FeedbackSample[] Data() => Enumerable.Range(0, 20).SelectMany(source =>
        Enumerable.Range(0, 10).Select(candidate => new FeedbackSample(
            $"source-{source}", $"query-{source}", $"candidate-{candidate}",
            candidate < 5 ? 1 : 2, candidate < 5 ? 1 : 2, null, 0, candidate < 5))).ToArray();
}
