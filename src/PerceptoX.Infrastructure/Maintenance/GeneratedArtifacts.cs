using System.Text.RegularExpressions;

namespace PerceptoX.Infrastructure.Maintenance;

internal static partial class GeneratedArtifacts
{
    private static readonly object MarkerGate = new();
    internal const string CacheMarker = ".perceptox-thumbnails-v1";
    internal const string ReportMarker = ".perceptox-report-v1";
    internal const string CacheSignature = "PerceptoX thumbnail cache v1";
    internal const string ReportSignature = "PerceptoX generated report v1";

    internal static void MarkCache(string root)
    {
        lock (MarkerGate) MarkCacheCore(root);
    }

    private static void MarkCacheCore(string root)
    {
        ManagedPaths.RejectLinks(root);
        Directory.CreateDirectory(root);
        string marker = Path.Combine(root, CacheMarker);
        ManagedPaths.RejectLinks(marker);
        if (File.Exists(marker))
        {
            if (File.ReadAllText(marker) != CacheSignature)
                throw new IOException("Marcajul cache-ului nu este valid.");
            return;
        }
        // Legacy caches remain usable, but are not adopted as wholly owned directories.
        if (Directory.EnumerateFileSystemEntries(root).Any()) return;
        using FileStream file = new(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using StreamWriter writer = new(file);
        writer.Write(CacheSignature);
    }

    internal static bool IsThumbnail(string relativePath) => ThumbnailPattern().IsMatch(relativePath.Replace('\\', '/'));

    [GeneratedRegex(@"^(?:queries/)?([0-9A-F]{2})/[1-9][0-9]*-\1[0-9A-F]{62}\.jpg$", RegexOptions.CultureInvariant)]
    private static partial Regex ThumbnailPattern();
}
