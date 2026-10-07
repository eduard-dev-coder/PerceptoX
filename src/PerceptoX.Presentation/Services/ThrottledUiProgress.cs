namespace PerceptoX.Presentation.Services;

/// <summary>Throttle before posting, so workers cannot create an unbounded dispatcher queue.</summary>
internal sealed class ThrottledUiProgress<T>(Action<T> callback, CancellationToken token) : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private long _last;
    public void Report(T value)
    {
        if (token.IsCancellationRequested) return;
        long now = Environment.TickCount64, previous = Interlocked.Read(ref _last);
        if (now - previous < 250 || Interlocked.CompareExchange(ref _last, now, previous) != previous) return;
        if (_context is null) { if (!token.IsCancellationRequested) callback(value); }
        else _context.Post(_ => { if (!token.IsCancellationRequested) callback(value); }, null);
    }
}
