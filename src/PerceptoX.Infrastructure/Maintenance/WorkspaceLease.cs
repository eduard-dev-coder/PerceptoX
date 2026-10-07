namespace PerceptoX.Infrastructure.Maintenance;

/// <summary>Cross-process exclusion shared by desktop workflows, cache readers and maintenance.</summary>
public sealed class WorkspaceLease : IDisposable
{
    private readonly List<FileStream> _locks = [];

    private WorkspaceLease() { }

    public static WorkspaceLease Acquire(string databasePath, string cacheRoot)
    {
        WorkspaceLease lease = new();
        try
        {
            string[] paths = [Path.GetFullPath(databasePath) + ".workspace.lock",
                ManagedPaths.Normalize(cacheRoot) + ".workspace.lock"];
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                ManagedPaths.RejectLinks(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                lease._locks.Add(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            return lease;
        }
        catch (IOException exception)
        {
            lease.Dispose();
            throw new IOException("Biblioteca/cache-ul este ocupat de altă operație. Așteptați finalizarea și reîncercați.", exception);
        }
        catch { lease.Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (FileStream file in _locks) file.Dispose();
        _locks.Clear();
    }
}
