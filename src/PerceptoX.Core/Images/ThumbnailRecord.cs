namespace PerceptoX.Core.Images;

public sealed record ThumbnailRecord
{
    public ThumbnailRecord(
        long imageId,
        string sourceVersion,
        string profileId,
        string relativePath,
        string formatId,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\') || relativePath.Contains(':') ||
            relativePath.Split('/').Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new ArgumentException("The cache path must be a normalized relative path without traversal.", nameof(relativePath));
        }

        ImageId = imageId;
        SourceVersion = sourceVersion;
        ProfileId = profileId;
        RelativePath = relativePath;
        FormatId = formatId;
        Width = width;
        Height = height;
    }

    public long ImageId { get; }
    public string SourceVersion { get; }
    public string ProfileId { get; }
    public string RelativePath { get; }
    public string FormatId { get; }
    public int Width { get; }
    public int Height { get; }
}
