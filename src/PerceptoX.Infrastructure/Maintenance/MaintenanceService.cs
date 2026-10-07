using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using PerceptoX.Application.Maintenance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PerceptoX.Infrastructure.Maintenance;

/// <summary>Deletes only a service-issued, confirmed inventory; never recursively deletes an arbitrary root.</summary>
public sealed partial class MaintenanceService(ILogger<MaintenanceService>? logger = null) : IMaintenanceService
{
    private readonly ConcurrentDictionary<Guid, MaintenancePlan> _plans = new();
    private readonly ILogger<MaintenanceService> _logger = logger ?? NullLogger<MaintenanceService>.Instance;

    public Task<StorageUsage> MeasureAsync(MaintenanceRequest request, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            Validate(request);
            using WorkspaceLease lease = WorkspaceLease.Acquire(request.DatabasePath, request.CacheRoot);
            cancellationToken.ThrowIfCancellationRequested();
            var index = ReadIndex(Path.GetFullPath(request.DatabasePath));
            ManagedPaths.ValidateTarget(request.CacheRoot, request.ProtectedRoots.Concat(index.Roots));
            long database = DatabaseFiles(request.DatabasePath).Sum(path => new FileInfo(path).Length);
            long cache = 0;
            int count = 0;
            foreach (string path in ManagedPaths.Files(request.CacheRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                cache += new FileInfo(path).Length;
                count++;
            }
            return new StorageUsage(database, cache, count);
        }, cancellationToken);

    public Task<MaintenancePlan> PreviewAsync(MaintenanceRequest request, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            Validate(request);
            using WorkspaceLease lease = WorkspaceLease.Acquire(request.DatabasePath, request.CacheRoot);
            using FileStream scanLock = AcquireScanLock(request.DatabasePath);
            MaintenanceRequest captured = request with { ProtectedRoots = Array.AsReadOnly(request.ProtectedRoots.ToArray()) };
            MaintenancePlan plan = Inventory(captured, cancellationToken);
            // One active preview avoids retaining several million-file inventories after cancelled dialogs.
            _plans.Clear();
            _plans[plan.Id] = plan;
            PreviewLogged(_logger, plan.Id, request.Level.ToString(), plan.Files.Count, plan.Bytes);
            return plan;
        }, cancellationToken);

    public Task<MaintenanceResult> ExecuteAsync(MaintenancePlan confirmedPlan, CancellationToken cancellationToken) =>
        Task.Run(() => Execute(confirmedPlan, cancellationToken), cancellationToken);

    private MaintenanceResult Execute(MaintenancePlan supplied, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        if (!_plans.TryRemove(supplied.Id, out MaintenancePlan? plan) || !ReferenceEquals(supplied, plan) ||
            DateTimeOffset.UtcNow - plan.CreatedUtc > TimeSpan.FromMinutes(10))
            throw new InvalidOperationException("Previzualizarea a expirat. Generați și confirmați o previzualizare nouă.");
        Validate(plan.Request);
        using WorkspaceLease lease = WorkspaceLease.Acquire(plan.Request.DatabasePath, plan.Request.CacheRoot);
        using FileStream scanLock = AcquireScanLock(plan.Request.DatabasePath);
        MaintenancePlan current = Inventory(plan.Request, token);
        // SQLite updates its shared-memory lock bookkeeping during a read. SHM is not image/index content.
        string sharedMemory = Path.GetFullPath(plan.Request.DatabasePath) + "-shm";
        bool sameFiles = plan.Files.Count == current.Files.Count && plan.Files.Zip(current.Files).All(pair =>
            pair.First.Path == pair.Second.Path && pair.First.Length == pair.Second.Length &&
            (pair.First.Path == sharedMemory || pair.First.LastWriteUtcTicks == pair.Second.LastWriteUtcTicks));
        if (!sameFiles || !plan.AffectedLibraries.SequenceEqual(current.AffectedLibraries))
            throw new IOException("Fișierele s-au schimbat după previzualizare. Confirmați o previzualizare nouă.");

        // Preflight every file before any deletion, with bounded handle usage for very large caches.
        int deleted = 0;
        long bytes = 0;
        List<string> errors = [];
        bool indexRemoved = false;
        foreach (MaintenanceFile file in current.Files)
        {
            token.ThrowIfCancellationRequested();
            ManagedPaths.RejectLinks(file.Path);
            using FileStream handle = new(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        StartedLogged(_logger, plan.Id, plan.Request.Level.ToString());

        // Remove the database first; never remove its WAL while retaining the database after a failure.
        string database = Path.GetFullPath(plan.Request.DatabasePath);
        IEnumerable<MaintenanceFile> ordered = current.Files.OrderBy(file =>
            string.Equals(file.Path, database, StringComparison.OrdinalIgnoreCase) ? 0 :
            file.Path == database + "-wal" || file.Path == database + "-shm" ? 1 : 2);
        bool cancelled = false;
        foreach (MaintenanceFile file in ordered)
        {
            bool sqliteSidecar = file.Path == database + "-wal" || file.Path == database + "-shm";
            if (token.IsCancellationRequested && !(indexRemoved && sqliteSidecar)) { cancelled = true; break; }
            try
            {
                ManagedPaths.RejectLinks(file.Path);
                FileInfo info = new(file.Path);
                if (!info.Exists || info.Length != file.Length || info.LastWriteTimeUtc.Ticks != file.LastWriteUtcTicks)
                    throw new IOException("Fișierul s-a modificat înainte de curățare.");
                File.Delete(file.Path);
                deleted++;
                bytes += file.Length;
                if (string.Equals(file.Path, Path.GetFullPath(plan.Request.DatabasePath), StringComparison.OrdinalIgnoreCase))
                    indexRemoved = true;
                plan.Request.Progress?.Report(new MaintenanceProgress(deleted, current.Files.Count, bytes));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{file.Path}: {exception.Message}");
                break;
            }
        }
        foreach (string directory in plan.Directories.OrderByDescending(path => path.Length))
        {
            try
            {
                ManagedPaths.RejectLinks(directory);
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, recursive: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { errors.Add($"{directory}: {exception.Message}"); }
        }
        FinishedLogged(_logger, plan.Id, deleted, bytes, errors.Count, cancelled);
        return new MaintenanceResult(deleted, bytes, cancelled, errors, indexRemoved);
    }

    [LoggerMessage(20, LogLevel.Information, "Maintenance preview {PlanId}: level {Level}, files {Files}, bytes {Bytes}")]
    private static partial void PreviewLogged(ILogger logger, Guid planId, string level, int files, long bytes);
    [LoggerMessage(21, LogLevel.Information, "Maintenance {PlanId} started: level {Level}")]
    private static partial void StartedLogged(ILogger logger, Guid planId, string level);
    [LoggerMessage(22, LogLevel.Information, "Maintenance {PlanId} finished: deleted {Deleted}, bytes {Bytes}, errors {Errors}, cancelled {Cancelled}")]
    private static partial void FinishedLogged(ILogger logger, Guid planId, int deleted, long bytes, int errors, bool cancelled);

    private static MaintenancePlan Inventory(MaintenanceRequest request, CancellationToken token)
    {
        string database = Path.GetFullPath(request.DatabasePath);
        string cache = ManagedPaths.Normalize(request.CacheRoot);
        (string[] roots, HashSet<string> indexedThumbnails) = ReadIndex(database);
        string[] protectedRoots = request.ProtectedRoots.Concat(roots).ToArray();
        ManagedPaths.ValidateTarget(cache, protectedRoots);
        ManagedPaths.ValidateTarget(database, protectedRoots);
        if (ManagedPaths.Contains(cache, database)) throw new IOException("Indexul nu poate fi în interiorul cache-ului.");
        List<string> targets = [];
        HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
        int preserved = 0;
        if (request.Level == MaintenanceLevel.Reports)
        {
            string reportRoot = ManagedPaths.Normalize(request.ReportRoot!);
            ManagedPaths.ValidateTarget(reportRoot, protectedRoots);
            if (ManagedPaths.Contains(reportRoot, cache) || ManagedPaths.Contains(cache, reportRoot) ||
                ManagedPaths.Contains(reportRoot, database))
                throw new IOException("Dosarul rapoartelor trebuie să fie separat de index și cache.");
            // Only generated, marked report subfolders. Unrelated CSV/HTML files are never selected.
            if (Directory.Exists(reportRoot))
                foreach (string directory in Directory.EnumerateDirectories(reportRoot, "PerceptoX-*"))
                {
                    token.ThrowIfCancellationRequested();
                    ManagedPaths.RejectLinks(directory);
                    string marker = Path.Combine(directory, GeneratedArtifacts.ReportMarker);
                    if (!File.Exists(marker) || File.ReadAllText(marker) != GeneratedArtifacts.ReportSignature) continue;
                    foreach (string name in new[] { "report.csv", "report.html" })
                        if (File.Exists(Path.Combine(directory, name))) targets.Add(Path.Combine(directory, name));
                }
        }
        else
        {
            string marker = Path.Combine(cache, GeneratedArtifacts.CacheMarker);
            bool owned = File.Exists(marker) && File.ReadAllText(marker) == GeneratedArtifacts.CacheSignature;
            foreach (string path in ManagedPaths.Files(cache))
            {
                token.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(cache, path).Replace('\\', '/');
                bool isMarker = relative == GeneratedArtifacts.CacheMarker || relative == "queries/" + GeneratedArtifacts.CacheMarker;
                if (GeneratedArtifacts.IsThumbnail(relative) && (owned || indexedThumbnails.Contains(relative)))
                    targets.Add(path);
                else if (!isMarker) preserved++;
            }
            if (preserved == 0 && owned)
            {
                targets.Add(marker);
                string queryMarker = Path.Combine(cache, "queries", GeneratedArtifacts.CacheMarker);
                if (File.Exists(queryMarker) && File.ReadAllText(queryMarker) == GeneratedArtifacts.CacheSignature)
                    targets.Add(queryMarker);
            }
            foreach (string path in targets)
                for (string? parent = Path.GetDirectoryName(path); parent is not null && ManagedPaths.Contains(cache, parent);
                    parent = Path.GetDirectoryName(parent)) directories.Add(parent);
            if (request.Level == MaintenanceLevel.IndexAndCache) targets.AddRange(DatabaseFiles(database));
        }
        MaintenanceFile[] files = targets.Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).Select(path =>
            {
                token.ThrowIfCancellationRequested();
                ManagedPaths.RejectLinks(path);
                FileInfo info = new(path);
                return new MaintenanceFile(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
            }).ToArray();
        return new MaintenancePlan(Guid.NewGuid(), request, Array.AsReadOnly(files),
            Array.AsReadOnly(directories.ToArray()), Array.AsReadOnly(roots), DateTimeOffset.UtcNow) { PreservedFiles = preserved };
    }

    private static (string[] Roots, HashSet<string> Thumbnails) ReadIndex(string database)
    {
        if (!File.Exists(database)) return ([], new HashSet<string>(StringComparer.Ordinal));
        ManagedPaths.RejectLinks(database);
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2 }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT RootPath FROM LibraryRoots ORDER BY RootPath;";
        using SqliteDataReader reader = command.ExecuteReader();
        List<string> roots = [];
        while (reader.Read()) roots.Add(reader.GetString(0));
        reader.Close();
        command.CommandText = "SELECT RelativePath FROM Thumbnails;";
        using SqliteDataReader thumbnails = command.ExecuteReader();
        HashSet<string> paths = new(StringComparer.Ordinal);
        while (thumbnails.Read()) paths.Add(thumbnails.GetString(0).Replace('\\', '/'));
        return (roots.ToArray(), paths);
    }

    private static void Validate(MaintenanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CacheRoot);
        if (request.Level == MaintenanceLevel.Reports) ArgumentException.ThrowIfNullOrWhiteSpace(request.ReportRoot);
        if (!Enum.IsDefined(request.Level)) throw new ArgumentOutOfRangeException(nameof(request));
        ManagedPaths.ValidateTarget(request.CacheRoot, request.ProtectedRoots);
        ManagedPaths.ValidateTarget(request.DatabasePath, request.ProtectedRoots);
    }

    private static FileStream AcquireScanLock(string database) =>
        new(Path.GetFullPath(database) + ".scan.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static IEnumerable<string> DatabaseFiles(string database) =>
        new[] { Path.GetFullPath(database), Path.GetFullPath(database) + "-wal", Path.GetFullPath(database) + "-shm" }
            .Where(File.Exists);
}
