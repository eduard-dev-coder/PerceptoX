using PerceptoX.Application.Matching;

namespace PerceptoX.Application.Calibration;

/// <summary>SourceKey identifies the original source across query variants; keys include file versions.
/// Only samples from the profile being calibrated may be passed together.</summary>
public sealed record FeedbackSample(
    string SourceKey,
    string QueryKey,
    string CandidateKey,
    int PerceptualDistance,
    int DifferenceDistance,
    int? MultiRegionBestDistance,
    int MultiRegionMatchedRegions,
    bool IsSimilar,
    int RegionMeasurementDistance = 1)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(QueryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(CandidateKey);
        ThresholdProfile.ValidateDistance(PerceptualDistance);
        ThresholdProfile.ValidateDistance(DifferenceDistance);
        ThresholdProfile.ValidateDistance(RegionMeasurementDistance);
        if (MultiRegionBestDistance is int distance) ThresholdProfile.ValidateDistance(distance);
        ArgumentOutOfRangeException.ThrowIfNegative(MultiRegionMatchedRegions);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MultiRegionMatchedRegions, 9);
        if (MultiRegionBestDistance is null && MultiRegionMatchedRegions != 0 ||
            MultiRegionBestDistance is int best &&
            ((best <= RegionMeasurementDistance) != (MultiRegionMatchedRegions > 0)))
            throw new ArgumentException("Regional distance and matched count are inconsistent.");
    }
}

public sealed record ThresholdProfile(
    string Id,
    int Version,
    DateTimeOffset CreatedAtUtc,
    string ProcessingProfileId,
    int MaxPerceptualDistance = 2,
    int MaxDifferenceDistance = 2,
    int MaximumRegionDistance = 1,
    int MinimumMatchedRegions = 1)
{
    public static ThresholdProfile CreateDefault(string processingProfileId) =>
        new("preset-default-v1", 1, DateTimeOffset.UnixEpoch, processingProfileId);

    public SimilaritySearchOptions ToSearchOptions(int topN = 50)
    {
        Validate();
        return new(topN, MaxPerceptualDistance, MaxDifferenceDistance);
    }

    public MultiRegionSearchOptions ToMultiRegionOptions(int topN = 200)
    {
        Validate();
        return new(topN, MaximumRegionDistance, MinimumMatchedRegions);
    }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProcessingProfileId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Version);
        if (CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Profile creation time must use UTC.");
        ValidateDistance(MaxPerceptualDistance);
        ValidateDistance(MaxDifferenceDistance);
        ValidateDistance(MaximumRegionDistance);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumMatchedRegions, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MinimumMatchedRegions, 9);
    }

    internal static void ValidateDistance(int distance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(distance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(distance, 64);
    }
}

public sealed record CalibrationEvaluation(int Samples, int Queries, int TruePositives,
    int FalsePositives, int TrueNegatives, int FalseNegatives)
{
    public int Positives => TruePositives + FalseNegatives;
    public int Negatives => TrueNegatives + FalsePositives;
    public double? Precision => TruePositives + FalsePositives == 0 ? null :
        (double)TruePositives / (TruePositives + FalsePositives);
    public double? Recall => Positives == 0 ? null : (double)TruePositives / Positives;
}

public enum CalibrationStatus { InsufficientData, Rejected, Validated }

public sealed record CalibrationProposal(
    CalibrationStatus Status,
    ThresholdProfile BaselineProfile,
    ThresholdProfile? ProposedProfile,
    CalibrationEvaluation BaselineHoldout,
    CalibrationEvaluation? ProposedHoldout,
    IReadOnlyList<string> TrainingQueryKeys,
    IReadOnlyList<string> HoldoutQueryKeys,
    IReadOnlyList<string> Warnings);

/// <summary>Conservative support gates, not a statistical guarantee of generalization.</summary>
public sealed record CalibrationOptions(
    int MinimumTrainingSamples = 100,
    int MinimumHoldoutSamples = 40,
    int MinimumTrainingQueries = 10,
    int MinimumHoldoutQueries = 5,
    int MinimumPositivesPerPartition = 20,
    int MinimumNegativesPerPartition = 20,
    int MaximumClassRatio = 10,
    int MaximumThresholdAdjustment = 4)
{
    internal void Validate()
    {
        // These floors cannot be bypassed by a caller to validate a handful of labels.
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumTrainingSamples, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumHoldoutSamples, 40);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumTrainingQueries, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumHoldoutQueries, 5);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumPositivesPerPartition, 20);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumNegativesPerPartition, 20);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumClassRatio, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumClassRatio, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumThresholdAdjustment, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumThresholdAdjustment, 8);
    }
}
