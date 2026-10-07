namespace PerceptoX.Application.Imaging;

public sealed record ImageProcessingRequest
{
    public ImageProcessingRequest(string sourcePath, long imageId, string sourceVersion, string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);

        SourcePath = Path.GetFullPath(sourcePath);
        ImageId = imageId;
        SourceVersion = sourceVersion;
        CacheRoot = Path.GetFullPath(cacheRoot);
    }

    public string SourcePath { get; }
    public long ImageId { get; }
    public string SourceVersion { get; }
    public string CacheRoot { get; }
}
