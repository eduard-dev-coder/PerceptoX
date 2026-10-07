using PerceptoX.Application.Calibration;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.Presentation.Tests;

public sealed class CalibrationViewModelTests
{
    [Fact]
    public async Task FeedbackRequiresFamilyStableVersionsAndHoldoutBeforeApply()
    {
        string root = Path.Combine(Path.GetTempPath(), "PerceptoX-feedback-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string query = Path.Combine(root, "query.png"), candidate = Path.Combine(root, "candidate.png");
            File.WriteAllBytes(query, [1]); File.WriteAllBytes(candidate, [2]);
            FakeStore store = new(); SettingsViewModel settings = new();
            CalibrationViewModel vm = new(store, settings, "pipeline-test", () => true);
            SimilarImageItem item = new(1, candidate, null, 99, 1, 1, 0, 2)
            { QueryVersion = CalibrationViewModel.Version(query), CandidateVersion = CalibrationViewModel.Version(candidate) };
            vm.Record(query, item, true, 1);
            Assert.Equal(0, store.Saves);
            vm.SourceFamily = "original-A";
            vm.Record(query, item, true, 1);
            Assert.Single(store.State!.Samples);
            vm.Record(query, item, false, 1);
            Assert.False(Assert.Single(store.State.Samples).IsSimilar);
            Assert.Equal(2, store.Saves);
            await vm.ProposeCommand.ExecuteAsync(null);
            vm.ApplyCommand.Execute(null);
            Assert.Equal(2, store.Saves);
            Assert.Equal(2, settings.MaxPerceptualDistance);
            File.AppendAllText(candidate, "changed");
            vm.Record(query, item, true, 1);
            Assert.Equal(2, store.Saves);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FakeStore : ICalibrationStore
    {
        public int Saves { get; private set; }
        public CalibrationState? State { get; private set; }
        public CalibrationState Load(ThresholdProfile preset) => new(1, new(preset), []);
        public void Save(CalibrationState state) { State = state; Saves++; }
    }
}
