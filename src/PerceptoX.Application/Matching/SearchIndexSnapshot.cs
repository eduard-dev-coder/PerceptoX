namespace PerceptoX.Application.Matching;

public readonly record struct SearchIndexEntry(
    long ImageId,
    ulong PerceptualHash,
    ulong DifferenceHash,
    int Width,
    int Height,
    long FileSize);

public sealed class SearchIndexSnapshot
{
    private readonly SearchIndexEntry[] _entries;

    public SearchIndexSnapshot(long scanId, string processingProfileId, ReadOnlySpan<SearchIndexEntry> entries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(processingProfileId);
        ScanId = scanId;
        ProcessingProfileId = processingProfileId;
        _entries = entries.ToArray();
        foreach (SearchIndexEntry entry in _entries)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entry.ImageId, nameof(entries));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entry.Width, nameof(entries));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entry.Height, nameof(entries));
            ArgumentOutOfRangeException.ThrowIfNegative(entry.FileSize, nameof(entries));
        }

        Array.Sort(_entries, static (left, right) => left.ImageId.CompareTo(right.ImageId));
        for (int index = 1; index < _entries.Length; index++)
        {
            if (_entries[index - 1].ImageId == _entries[index].ImageId)
            {
                throw new ArgumentException("Snapshot entries must have unique image IDs.", nameof(entries));
            }
        }
    }

    public long ScanId { get; }
    public string ProcessingProfileId { get; }
    public int Count => _entries.Length;
    public ReadOnlySpan<SearchIndexEntry> Entries => _entries;

    public bool TryGetById(long imageId, out SearchIndexEntry entry)
    {
        int low = 0;
        int high = _entries.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            SearchIndexEntry candidate = _entries[middle];
            if (candidate.ImageId == imageId)
            {
                entry = candidate;
                return true;
            }

            if (candidate.ImageId < imageId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        entry = default;
        return false;
    }
}
