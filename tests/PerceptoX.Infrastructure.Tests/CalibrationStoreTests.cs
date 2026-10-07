using PerceptoX.Application.Calibration;
using PerceptoX.Infrastructure.Calibration;
namespace PerceptoX.Infrastructure.Tests;

public sealed class CalibrationStoreTests
{
    [Fact]
    public void JsonStateRoundtripsAndRejectsOtherProcessingProfileAndConcurrentOverwrite()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-calibration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "feedback.json");
            JsonCalibrationStore store = new(path);
            var preset = ThresholdProfile.CreateDefault("pipeline-test-v1");
            var initial = store.Load(preset);
            FeedbackSample sample = new("source-family", "query", "candidate", 1, 2, 0, 2, true);
            store.Save(initial with { Samples = [sample] });
            var loaded = store.Load(preset);
            Assert.Equal(sample, Assert.Single(loaded.Samples));
            Assert.Equal(preset, loaded.Selection.Active);
            JsonCalibrationStore incompatible = new(path);
            Assert.Throws<InvalidDataException>(() => incompatible.Load(ThresholdProfile.CreateDefault("other-pipeline")));
            Assert.Throws<InvalidOperationException>(() => incompatible.Save(initial));
            File.AppendAllText(path, " ");
            Assert.Throws<IOException>(() => store.Save(initial));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
