namespace PerceptoX.Application.Indexing;

public sealed record IndexingRequest
{
    public IndexingRequest(string libraryRoot, string databasePath, string thumbnailCacheRoot, string processingProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailCacheRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(processingProfileId);

        LibraryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        DatabasePath = Path.GetFullPath(databasePath);
        ThumbnailCacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(thumbnailCacheRoot));
        ProcessingProfileId = processingProfileId;

        if (IsWithin(LibraryRoot, DatabasePath) || IsWithin(LibraryRoot, ThumbnailCacheRoot))
        {
            throw new ArgumentException("The database and thumbnail cache must be outside the library root.");
        }
    }

    public string LibraryRoot { get; }
    public string DatabasePath { get; }
    public string ThumbnailCacheRoot { get; }
    public string ProcessingProfileId { get; }

    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
