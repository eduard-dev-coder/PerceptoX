namespace PerceptoX.Infrastructure.Imaging;

internal sealed class DecodedMemoryBudget
{
    private readonly object _gate = new();
    private readonly LinkedList<Waiter> _waiters = new();
    private long _usedBytes;

    public DecodedMemoryBudget(long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        CapacityBytes = capacityBytes;
    }

    public long CapacityBytes { get; }

    public ValueTask<Lease> AcquireAsync(long bytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        if (bytes > CapacityBytes)
        {
            throw new InvalidDataException("Image exceeds the configured decoded-memory budget.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_waiters.Count == 0 && bytes <= CapacityBytes - _usedBytes)
            {
                _usedBytes += bytes;
                return ValueTask.FromResult(new Lease(this, bytes));
            }

            Waiter waiter = new(bytes);
            waiter.Node = _waiters.AddLast(waiter);
            return new ValueTask<Lease>(WaitAsync(waiter, cancellationToken));
        }
    }

    private async Task<Lease> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(() => Cancel(waiter, cancellationToken));
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    private void Cancel(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (waiter.Node?.List is null)
            {
                return;
            }

            _waiters.Remove(waiter.Node);
            waiter.Completion.TrySetCanceled(cancellationToken);
            AdmitWaiters();
        }
    }

    private void Release(long bytes)
    {
        lock (_gate)
        {
            _usedBytes -= bytes;
            AdmitWaiters();
        }
    }

    private void AdmitWaiters()
    {
        while (_waiters.First is { } node && node.Value.Bytes <= CapacityBytes - _usedBytes)
        {
            _waiters.RemoveFirst();
            _usedBytes += node.Value.Bytes;
            node.Value.Completion.TrySetResult(new Lease(this, node.Value.Bytes));
        }
    }

    private sealed class Waiter(long bytes)
    {
        public long Bytes { get; } = bytes;
        public LinkedListNode<Waiter>? Node { get; set; }
        public TaskCompletionSource<Lease> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class Lease(DecodedMemoryBudget owner, long bytes) : IDisposable
    {
        private DecodedMemoryBudget? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(bytes);
    }
}
