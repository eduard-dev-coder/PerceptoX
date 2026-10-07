using PerceptoX.Presentation.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using PerceptoX.Application.Indexing;

namespace PerceptoX.Presentation.ViewModels;

public enum ScanOutcome { Ready, Running, Succeeded, Cancelled, Failed }

public sealed record ScanProgressSnapshot(ScanOutcome Outcome, string StageLabel, string FileName,
    int Completed, int? Total, int Persisted, int Errors, int Unsupported, int Inaccessible,
    TimeSpan Elapsed, TimeSpan? Eta, string Summary, bool CancellationRequested, string Activity = "Library")
{
    public bool IsRunning => Outcome == ScanOutcome.Running;
    public bool HasFile => IsRunning && FileName.Length > 0;
    public bool IsIndeterminate => Total is null or <= 0;
    public double Percent => Total is > 0 ? Math.Clamp(Completed * 100d / Total.Value, 0, 100) : 0;
    public string CountsLabel => Total is int total
        ? UiText.T("ScanProgressViewModel.Text001", Completed, total, Math.Max(0, total - Completed))
        : UiText.T("ScanProgressViewModel.Text002", Completed);
    public string DetailsLabel => Activity == "Queries" ? UiText.T("ScanProgressViewModel.Text003", Errors) :
        UiText.T("ScanProgressViewModel.Text004", Persisted, Errors, Unsupported, Inaccessible);
    public string ElapsedLabel => UiText.T("ScanProgressViewModel.Text005", Elapsed);
    public string EtaLabel => Eta is TimeSpan eta ? UiText.T("ScanProgressViewModel.Text006", eta) : UiText.T("ScanProgressViewModel.Text007");
}

// Worker reports only replace one snapshot. Refresh is called on the UI thread,
// avoiding one dispatcher item/event for every file and unbounded UI queues.
public sealed class ScanProgressViewModel(TimeProvider? timeProvider = null) : ObservableObject, IProgress<IndexingProgress>
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private IndexingProgress? _latest;
    private long _started, _phaseStarted;
    private string? _activity;
    private bool _running, _cancelRequested;
    private ScanProgressSnapshot _snapshot = new(ScanOutcome.Ready, UiText.T("ScanProgressViewModel.Text008"), "", 0, null, 0, 0, 0, 0, TimeSpan.Zero, null, "", false);
    public ScanProgressSnapshot Snapshot { get => _snapshot; private set => SetProperty(ref _snapshot, value); }

    public void Begin()
    {
        lock (_gate)
        {
            _running = true; _cancelRequested = false; _latest = null; _activity = null;
            _started = _phaseStarted = _clock.GetTimestamp();
        }
        Refresh();
    }

    public void Report(IndexingProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (!_running) return;
            if (_activity != value.Activity) { _activity = value.Activity; _phaseStarted = _clock.GetTimestamp(); }
            _latest = value;
        }
    }

    public void RequestCancellation()
    {
        lock (_gate) { if (!_running) return; _cancelRequested = true; }
        Refresh();
    }

    public void Refresh()
    {
        ScanProgressSnapshot snapshot;
        lock (_gate)
        {
            if (!_running) return;
            IndexingProgress? progress = _latest;
            int completed = progress is null ? 0 : progress.Processed + progress.Failed + progress.SkippedUnchanged;
            int? total = progress?.Total;
            TimeSpan phase = _clock.GetElapsedTime(_phaseStarted);
            double remainingSeconds = total is > 0 && completed > 0
                ? phase.TotalSeconds * Math.Max(0, total.Value - completed) / completed : double.NaN;
            TimeSpan? eta = double.IsFinite(remainingSeconds) && remainingSeconds <= TimeSpan.MaxValue.TotalSeconds
                ? TimeSpan.FromSeconds(remainingSeconds) : null;
            snapshot = new(ScanOutcome.Running, _cancelRequested ? UiText.T("ScanProgressViewModel.Text009") : StageLabel(progress),
                progress?.CurrentFileName ?? "", completed, total, progress?.Persisted ?? 0,
                progress?.Failed ?? 0, progress?.Unsupported ?? 0, progress?.Inaccessible ?? 0,
                _clock.GetElapsedTime(_started), eta, UiText.T("ScanProgressViewModel.Text010"), _cancelRequested, progress?.Activity ?? "Library");
        }
        Snapshot = snapshot;
    }

    public void Complete(ScanOutcome outcome, string summary)
    {
        if (outcome is ScanOutcome.Ready or ScanOutcome.Running) throw new ArgumentOutOfRangeException(nameof(outcome));
        Refresh();
        lock (_gate) { _running = false; }
        Snapshot = Snapshot with { Outcome = outcome, FileName = "", Eta = null, Summary = summary,
            Errors = Snapshot.Errors + (outcome == ScanOutcome.Failed ? 1 : 0),
            StageLabel = outcome switch { ScanOutcome.Succeeded => UiText.T("ScanProgressViewModel.Text011"), ScanOutcome.Cancelled => UiText.T("ScanProgressViewModel.Text012"), _ => UiText.T("ScanProgressViewModel.Text013") } };
    }

    private static string StageLabel(IndexingProgress? progress)
    {
        string scope = progress?.Activity == "Queries" ? UiText.T("ScanProgressViewModel.Text014") : UiText.T("ScanProgressViewModel.Text015");
        string stage = progress?.Stage switch
        {
            "Discovery" => UiText.T("ScanProgressViewModel.Text016"),
            "Processing" => UiText.T("ScanProgressViewModel.Text017"),
            "Persistence" => UiText.T("ScanProgressViewModel.Text018"),
            "Publishing" => UiText.T("ScanProgressViewModel.Text019"),
            "Search" => UiText.T("ScanProgressViewModel.Text020"),
            "Loading" => UiText.T("ScanProgressViewModel.Text021"),
            "Results" => UiText.T("ScanProgressViewModel.Text022"),
            "Completed" or "Partial" => UiText.T("ScanProgressViewModel.Text023"),
            _ => UiText.T("ScanProgressViewModel.Text008")
        };
        return $"{scope}: {stage}";
    }
}
