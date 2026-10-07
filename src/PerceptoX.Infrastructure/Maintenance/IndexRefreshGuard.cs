using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerceptoX.Infrastructure.Maintenance;

/// <summary>Durable pending-library marker. Callers must hold the database/cache WorkspaceLease.</summary>
public static class IndexRefreshGuard
{
    public static void Mark(string databasePath, IEnumerable<string> libraryRoots)
    {
        ArgumentNullException.ThrowIfNull(libraryRoots);
        string path = MarkerPath(databasePath);
        string[] roots = NormalizeRoots(libraryRoots);
        if (roots.Length == 0) throw new ArgumentException("At least one library root is required.", nameof(libraryRoots));
        bool exists = File.Exists(path);
        if (exists) roots = NormalizeRoots(Read(path).LibraryRoots!.Concat(roots));
        Publish(path, new Marker { SchemaVersion = 1, LibraryRoots = roots }, exists);
    }

    /// <summary>Called only after a complete scan commits under the same WorkspaceLease.</summary>
    public static void CompletedScan(string databasePath, string libraryRoot)
    {
        string path = MarkerPath(databasePath);
        if (!File.Exists(path)) return;
        string completed = NormalizeRoots([libraryRoot])[0];
        Marker marker = Read(path);
        string[] remaining = marker.LibraryRoots!.Where(root => !string.Equals(root, completed, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (remaining.Length == marker.LibraryRoots!.Length) return;
        ManagedPaths.RejectLinks(path);
        if (remaining.Length == 0) File.Delete(path);
        else Publish(path, new Marker { SchemaVersion = 1, LibraryRoots = remaining }, exists: true);
    }

    private static string MarkerPath(string databasePath)
    {
        string database = ManagedPaths.Normalize(databasePath);
        ManagedPaths.RejectLinks(database);
        string marker = database + ".refresh-required";
        ManagedPaths.RejectLinks(marker);
        if (Directory.Exists(marker)) throw new IOException("Index refresh marker is a directory.");
        return marker;
    }

    private static string[] NormalizeRoots(IEnumerable<string> roots)
    {
        List<string> normalized = [];
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new IOException("Index refresh marker has an empty library root.");
            string path = ManagedPaths.Normalize(root);
            ManagedPaths.RejectLinks(path);
            normalized.Add(path);
        }
        return normalized.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Marker Read(string path)
    {
        ManagedPaths.RejectLinks(path);
        Marker marker;
        try { marker = JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(path)) ?? throw new IOException("Empty index refresh marker."); }
        catch (JsonException exception) { throw new IOException("Corrupt or unknown index refresh marker; preserved for recovery.", exception); }
        if (marker.SchemaVersion != 1 || marker.LibraryRoots is null || marker.LibraryRoots.Length == 0)
            throw new IOException("Unknown index refresh marker schema; preserved for recovery.");
        string[] normalized = NormalizeRoots(marker.LibraryRoots);
        if (!normalized.SequenceEqual(marker.LibraryRoots, StringComparer.Ordinal))
            throw new IOException("Index refresh marker roots are not normalized and distinct.");
        return marker;
    }

    private static void Publish(string path, Marker marker, bool exists)
    {
        ManagedPaths.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, marker);
                stream.Flush(flushToDisk: true);
            }
            ManagedPaths.RejectLinks(path);
            File.Move(temporary, path, overwrite: exists);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class Marker
    {
        public int SchemaVersion { get; set; }
        public string[]? LibraryRoots { get; set; }
    }
}
