namespace PerceptoX.Infrastructure.Maintenance;

public static class ManagedPaths
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool Contains(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    public static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Cale cu link/junction refuzată: {current}");
    }

    public static void ValidateTarget(string target, IEnumerable<string> protectedRoots)
    {
        string full = Normalize(target);
        if (full == Normalize(Path.GetPathRoot(full)!) ||
            Contains(full, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ||
            Contains(full, AppContext.BaseDirectory))
            throw new IOException("Ținta de curățare este un director prea larg.");
        foreach (string root in protectedRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
            if (Contains(full, Normalize(root)) || Contains(Normalize(root), full))
                throw new IOException("Datele aplicației se suprapun cu un dosar protejat de fotografii.");
        RejectLinks(full);
    }

    public static IEnumerable<string> Files(string root)
    {
        RejectLinks(root);
        if (!Directory.Exists(root)) yield break;
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            RejectLinks(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLinks(path);
                if (Directory.Exists(path)) pending.Push(path);
                else yield return path;
            }
        }
    }
}
