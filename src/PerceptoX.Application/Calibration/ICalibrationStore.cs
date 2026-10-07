namespace PerceptoX.Application.Calibration;

public sealed record CalibrationState(int SchemaVersion, ProfileSelection Selection, IReadOnlyList<FeedbackSample> Samples);
public interface ICalibrationStore
{
    CalibrationState Load(ThresholdProfile preset);
    void Save(CalibrationState state);
}
