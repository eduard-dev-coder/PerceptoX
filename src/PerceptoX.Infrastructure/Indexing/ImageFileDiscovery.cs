namespace PerceptoX.Infrastructure.Indexing;

public sealed record ImageDiscoveryIssue(string Path, string ErrorCode, bool Unsupported);

public static class ImageFileDiscovery
{
    public static IEnumerable<string> EnumerateSupportedImages(string root, Action<ImageDiscoveryIssue>? onIssue = null,
        Func<string, bool>? additionalFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot) ||
            (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The image root must be an existing, non-reparse directory.");
        }

        return EnumerateCore(fullRoot, onIssue, additionalFormat);
    }

    private static IEnumerable<string> EnumerateCore(string fullRoot, Action<ImageDiscoveryIssue>? onIssue, Func<string, bool>? additionalFormat)
    {
        Stack<string> directories = new();
        directories.Push(fullRoot);
        while (directories.TryPop(out string? directory))
        {
            IEnumerator<string>? iterator = null;
            try { iterator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (onIssue is null) throw;
                onIssue(new ImageDiscoveryIssue(directory, exception.GetType().Name, false));
            }
            if (iterator is null) continue;
            using (iterator)
            {
                while (true)
                {
                    string? path = null;
                    try { if (iterator.MoveNext()) path = iterator.Current; }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        if (onIssue is null) throw;
                        onIssue(new ImageDiscoveryIssue(directory, exception.GetType().Name, false));
                    }
                    if (path is null) break;
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(path); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        if (onIssue is null) throw;
                        onIssue(new ImageDiscoveryIssue(path, exception.GetType().Name, false));
                        continue;
                    }
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) { directories.Push(path); continue; }
                    if (IsSupportedImage(path) || additionalFormat?.Invoke(path) == true) yield return path;
                    else if (IsRecognizedUnsupportedImage(path)) onIssue?.Invoke(new ImageDiscoveryIssue(path, "UnsupportedFormat", true));
                }
            }
        }
    }

    public static bool IsRecognizedUnsupportedImage(string path) =>
        Path.GetExtension(path).ToUpperInvariant() is ".HEIC" or ".HEIF" or ".AVIF";

    public static bool IsSupportedImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetExtension(path) switch
        {
            string extension when extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) => true,
            string extension when extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) => true,
            string extension when extension.Equals(".png", StringComparison.OrdinalIgnoreCase) => true,
            string extension when extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) => true,
            string extension when extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) => true,
            string extension when extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) => true,
            string extension when extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) => true,
            string extension when extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }
}
