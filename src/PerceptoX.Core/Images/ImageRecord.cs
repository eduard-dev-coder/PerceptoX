namespace PerceptoX.Core.Images;

public sealed record ImageRecord
{
    public ImageRecord(
        long id,
        long rootId,
        string pathKey,
        string filePath,
        int width,
        int height,
        long fileSize,
        DateTimeOffset lastWriteTimeUtc,
        bool isActive = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);

        if (!Path.IsPathFullyQualified(filePath) || string.IsNullOrWhiteSpace(Path.GetFileName(filePath)))
        {
            throw new ArgumentException("The image path must be an absolute file path.", nameof(filePath));
        }

        if (lastWriteTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The last-write timestamp must use UTC offset zero.", nameof(lastWriteTimeUtc));
        }

        Id = id;
        RootId = rootId;
        PathKey = pathKey;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Width = width;
        Height = height;
        FileSize = fileSize;
        LastWriteTimeUtc = lastWriteTimeUtc;
        IsActive = isActive;
    }

    public long Id { get; }
    public long RootId { get; }
    public string PathKey { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public int Width { get; }
    public int Height { get; }
    public double AspectRatio => (double)Width / Height;
    public long FileSize { get; }
    public DateTimeOffset LastWriteTimeUtc { get; }
    public bool IsActive { get; }
}
