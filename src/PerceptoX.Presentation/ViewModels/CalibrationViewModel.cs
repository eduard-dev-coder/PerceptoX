using PerceptoX.Presentation.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PerceptoX.Application.Calibration;
using PerceptoX.Presentation.Services;
using System.Security.Cryptography;
using System.Text;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class CalibrationViewModel : LocalizedViewModel
{
    private void SetStatus(Func<string> render) => SetLocalized(nameof(StatusMessage), value => StatusMessage = value, render);
    private void AppendStatus(Func<string> render) => AppendLocalized(nameof(StatusMessage), value => StatusMessage = value, render);

    private readonly ICalibrationStore _store;
    private readonly SettingsViewModel _settings;
    private readonly Func<bool> _idle;
    private readonly ThresholdProfile _preset;
    private CalibrationState _state;
    private CalibrationProposal? _proposal;
    public CalibrationViewModel(ICalibrationStore store, SettingsViewModel settings, string processingProfile, Func<bool> idle)
    {
        _store = store; _settings = settings; _idle = idle;
        _preset = ThresholdProfile.CreateDefault(processingProfile);
        _state = new(1, new ProfileSelection(_preset), []);
        SetStatus(() => UiText.T("CalibrationViewModel.Text002"));
        try { _state = store.Load(_preset); SetThresholds(_state.Selection.Active); }
        catch (Exception error) { SetStatus(() => UiText.T("CalibrationViewModel.Text001", error.Message)); }
    }

    [ObservableProperty] public partial string SourceFamily { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string StatusMessage { get; private set; } = UiText.T("CalibrationViewModel.Text002");

    public void Record(string queryPath, SimilarImageItem candidate, bool similar, int measuredRegionDistance)
    {
        if (!_idle() || IsBusy) return;
        try
        {
            if (string.IsNullOrWhiteSpace(SourceFamily))
                throw new InvalidOperationException(UiText.T("CalibrationViewModel.Text003"));
            if (candidate.QueryVersion != Version(queryPath) || candidate.CandidateVersion != Version(candidate.FilePath))
                throw new InvalidOperationException(UiText.T("CalibrationViewModel.Text004"));
            if (measuredRegionDistance != candidate.RegionMeasurementDistance)
                throw new InvalidOperationException(UiText.T("CalibrationViewModel.Text005"));
            if (SourceFamily.Length > 200) throw new InvalidOperationException(UiText.T("CalibrationViewModel.Text006"));
            FeedbackSample sample = new(SourceFamily.Trim().ToUpperInvariant(), Key(candidate.QueryVersion), Key(candidate.CandidateVersion),
                candidate.PerceptualDistance, candidate.DifferenceDistance, candidate.MultiRegionBestDistance,
                candidate.MultiRegionMatchedRegions, similar, measuredRegionDistance);
            var samples = _state.Samples.Where(value => value.QueryKey != sample.QueryKey || value.CandidateKey != sample.CandidateKey)
                .Append(sample).ToArray();
            if (samples.Length > 20_000) throw new InvalidOperationException(UiText.T("CalibrationViewModel.Text007"));
            CalibrationState next = _state with { Samples = samples };
            _store.Save(next); _state = next; _proposal = null;
            SetStatus(() => UiText.T("CalibrationViewModel.Text008", samples.Length));
        }
        catch (Exception error) { SetStatus(() => error.Message); }
    }

    [RelayCommand] private async Task ProposeAsync()
    {
        if (!_idle() || IsBusy) return;
        IsBusy = true;
        try
        {
            if (!MatchesSettings(_state.Selection.Active)) throw new InvalidOperationException(UiText.T("CalibrationViewModel.Text009"));
            _proposal = await Task.Run(() => ThresholdCalibrator.Propose(_state.Samples, _state.Selection.Active,
                "user-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow));
            SetStatus(() => UiText.T("CalibrationViewModel.Text010", UiText.T("Calibration.Status." + _proposal.Status)) + string.Join(' ', _proposal.Warnings.Select(LocalizeWarning)));
            if (_proposal.ProposedProfile is { } proposed)
                AppendStatus(() => UiText.T("CalibrationViewModel.Text011", proposed.MaxPerceptualDistance, proposed.MaxDifferenceDistance, proposed.MaximumRegionDistance, proposed.MinimumMatchedRegions));
            if (_proposal.ProposedHoldout is { } evaluation)
                AppendStatus(() => UiText.T("CalibrationViewModel.Text012", evaluation.Samples, evaluation.FalsePositives, evaluation.FalseNegatives));
        }
        catch (Exception error) { SetStatus(() => error.Message); }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void Apply() => Change(() => _state.Selection.Apply(_proposal ?? throw new InvalidOperationException(UiText.T("CalibrationViewModel.Text013"))));
    [RelayCommand] private void Rollback() => Change(() => _state.Selection.Rollback());
    [RelayCommand] private void Reset() => Change(() => _state.Selection.Reset(_preset));
    [RelayCommand] private void RestoreActive() { if (_idle()) { SetThresholds(_state.Selection.Active); SetStatus(() => UiText.T("CalibrationViewModel.Text014")); } }
    private void Change(Func<ProfileSelection> select)
    {
        if (!_idle() || IsBusy) return;
        try
        {
            CalibrationState next = _state with { Selection = select() };
            _store.Save(next); _state = next; _proposal = null; SetThresholds(next.Selection.Active);
            SetStatus(() => UiText.T("CalibrationViewModel.Text015", next.Selection.Active.Id, next.Selection.Active.Version));
        }
        catch (Exception error) { SetStatus(() => error.Message); }
    }
    private bool MatchesSettings(ThresholdProfile profile) => _settings.MaxPerceptualDistance == profile.MaxPerceptualDistance
        && _settings.MaxDifferenceDistance == profile.MaxDifferenceDistance && _settings.MaxMultiRegionDistance == profile.MaximumRegionDistance
        && _settings.MinimumMatchedRegions == profile.MinimumMatchedRegions;
    private void SetThresholds(ThresholdProfile profile) =>
        (_settings.MaxPerceptualDistance, _settings.MaxDifferenceDistance, _settings.MaxMultiRegionDistance, _settings.MinimumMatchedRegions) =
        (profile.MaxPerceptualDistance, profile.MaxDifferenceDistance, profile.MaximumRegionDistance, profile.MinimumMatchedRegions);
    public static string Version(string path) { FileInfo info = new(path); return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"; }
    private static string Key(string version) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version)));

    private static string LocalizeWarning(string warning) => warning switch
    {
        "Metrics describe labeled holdout pairs only; they are not match probabilities or whole-library recall." => UiText.T("Calibration.Warning.Metrics"),
        "Regional distance is fixed at its measurement threshold; only minimum matched regions can be tuned." => UiText.T("Calibration.Warning.Regions"),
        "Reusing this holdout repeatedly for manual tuning can bias evaluation; use fresh independent labels." => UiText.T("Calibration.Warning.Reuse"),
        "Insufficient data: each partition requires its sample/query minimum, at least 20 positives and 20 negatives, and a class ratio no greater than 10:1." => UiText.T("Calibration.Warning.Data"),
        "Insufficient regional labels: minimum matched regions remains at the baseline value." => UiText.T("Calibration.Warning.Labels"),
        "Holdout regression or undefined precision: proposal cannot be activated." => UiText.T("Calibration.Warning.Rejected"),
        _ => warning
    };
}
