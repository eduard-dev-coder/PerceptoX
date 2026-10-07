using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using PerceptoX.Application.Cleanup;
using PerceptoX.Infrastructure.Maintenance;

namespace PerceptoX.Infrastructure.Cleanup;

/// <summary>Same-volume recoverable renames of explicitly verified extras. Never deletes originals.</summary>
public sealed class RecoverableSmartCleanupService : ISmartCleanupService
{
    private readonly string _databasePath;
    private readonly string _cacheRoot;
    private readonly string[] _libraryRoots;
    private readonly string _archiveRoot;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, IssuedPlan> _plans = new();

    public RecoverableSmartCleanupService(string databasePath, string cacheRoot, IEnumerable<string> libraryRoots,
        string archiveRoot, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(libraryRoots);
        _databasePath = ManagedPaths.Normalize(databasePath);
        _cacheRoot = ManagedPaths.Normalize(cacheRoot);
        _libraryRoots = libraryRoots.Select(ManagedPaths.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _archiveRoot = ManagedPaths.Normalize(archiveRoot);
        _time = timeProvider ?? TimeProvider.System;
        if (_libraryRoots.Length == 0) throw new ArgumentException("A photo library is required.", nameof(libraryRoots));
        ValidateRoots();
    }

    public async Task<SmartCleanupPlan> PreviewAsync(IReadOnlyList<VerifiedCleanupGroup> groups, KeeperRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (!Enum.IsDefined(rule)) throw new ArgumentOutOfRangeException(nameof(rule));
        ValidateRoots();
        using WorkspaceLease lease = WorkspaceLease.Acquire(_databasePath, _cacheRoot);
        Guid id = Guid.NewGuid();
        List<CleanupKeeper> keepers = [];
        List<CleanupMove> moves = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (VerifiedCleanupGroup group in groups)
        {
            if (!group.ManuallyVerified || group.Files.Count < 2)
                throw new ArgumentException("Only manually verified groups with at least two files are accepted.", nameof(groups));
            List<(CleanupFileCandidate Candidate, CleanupKeeper Snapshot)> files = [];
            foreach (CleanupFileCandidate candidate in group.Files)
            {
                string source = ValidateSource(candidate.Path);
                if (candidate.Width <= 0 || candidate.Height <= 0 || !seen.Add(source))
                    throw new ArgumentException("Invalid dimensions or duplicate cleanup source.", nameof(groups));
                CleanupKeeper snapshot = await SnapshotAsync(source, cancellationToken).ConfigureAwait(false);
                if (candidate.LastWriteTimeUtc.Kind != DateTimeKind.Utc || snapshot.LastWriteTimeUtc != candidate.LastWriteTimeUtc)
                    throw new IOException($"File changed since group verification: {source}");
                files.Add((candidate, snapshot));
            }
            var ranked = rule == KeeperRule.HighestResolution
                ? files.OrderByDescending(file => (long)file.Candidate.Width * file.Candidate.Height).ThenByDescending(file => file.Snapshot.LastWriteTimeUtc)
                : files.OrderByDescending(file => file.Snapshot.LastWriteTimeUtc).ThenByDescending(file => (long)file.Candidate.Width * file.Candidate.Height);
            var ordered = ranked.ThenBy(file => file.Snapshot.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            keepers.Add(ordered[0].Snapshot);
            foreach (var file in ordered.Skip(1))
            {
                CleanupKeeper snapshot = file.Snapshot;
                if (!string.Equals(Path.GetPathRoot(snapshot.Path), Path.GetPathRoot(_archiveRoot), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Archive must be on the same volume as all sources for atomic recovery.");
                string archive = Path.Combine(OperationDirectory(id), "files", moves.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), Path.GetFileName(snapshot.Path));
                moves.Add(new(snapshot.Path, archive, snapshot.Size, snapshot.LastWriteTimeUtc, snapshot.Sha256));
            }
        }
        if (moves.Count == 0) throw new ArgumentException("No verified cleanup files supplied.", nameof(groups));
        IssuedPlan plan = new(id, _time.GetUtcNow().AddMinutes(10), keepers.ToArray(), moves.ToArray());
        foreach (var expired in _plans.Where(item => item.Value.ExpiresAt <= _time.GetUtcNow())) _plans.TryRemove(expired.Key, out _);
        _plans[id] = plan;
        return plan;
    }

    public async Task<CleanupResult> ExecuteAsync(SmartCleanupPlan plan, IProgress<CleanupEntryResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Recoverable cleanup requires Windows handle-based rename.");
        if (!_plans.TryGetValue(plan.OperationId, out IssuedPlan? issued) || !ReferenceEquals(plan, issued) || issued.ExpiresAt <= _time.GetUtcNow())
            throw new InvalidOperationException("Preview is foreign, expired or already executed. Preview and confirm again.");
        ValidateRoots();
        using WorkspaceLease lease = WorkspaceLease.Acquire(_databasePath, _cacheRoot);
        cancellationToken.ThrowIfCancellationRequested();
        List<FileStream> sourceLocks = [];
        Dictionary<string, FileStream> moveLocks = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (CleanupKeeper keeper in issued.Keepers)
                sourceLocks.Add(await LockVerifiedAsync(keeper.Path, keeper.Size, keeper.LastWriteTimeUtc, keeper.Sha256, false, cancellationToken).ConfigureAwait(false));
            foreach (CleanupMove move in issued.Moves)
            {
                ValidateMove(issued.OperationId, move);
                if (Path.Exists(move.ArchivePath)) throw new IOException($"Archive collision: {move.ArchivePath}");
                FileStream moveLock = await LockVerifiedAsync(move.OriginalPath, move.Size, move.LastWriteTimeUtc, move.Sha256, true, cancellationToken).ConfigureAwait(false);
                sourceLocks.Add(moveLock);
                moveLocks.Add(move.OriginalPath, moveLock);
            }
            if (issued.ExpiresAt <= _time.GetUtcNow()) throw new InvalidOperationException("Preview expired during revalidation. Preview and confirm again.");
            if (!_plans.TryRemove(issued.OperationId, out _)) throw new InvalidOperationException("Preview already executed.");
            Journal journal = new() { OperationId = issued.OperationId, DatabasePath = _databasePath, CacheRoot = _cacheRoot,
                LibraryRoots = _libraryRoots, ArchiveRoot = _archiveRoot, Moves = issued.Moves.ToArray(), Keepers = issued.Keepers.ToArray() };
            SaveJournal(journal, create: true);
            IndexRefreshGuard.Mark(_databasePath, _libraryRoots);
            foreach (CleanupMove move in journal.Moves)
            {
                if (cancellationToken.IsCancellationRequested) { journal.Cancelled = true; break; }
                try
                {
                    ValidateMove(journal.OperationId, move);
                    Directory.CreateDirectory(Path.GetDirectoryName(move.ArchivePath)!);
                    ManagedPaths.RejectLinks(move.ArchivePath);
                    WindowsFileRename.Move(moveLocks[move.OriginalPath].SafeFileHandle, move.ArchivePath);
                    journal.Changed = true;
                    SaveJournal(journal, create: false);
                    progress?.Report(new(move, CleanupEntryState.Archived, null));
                }
                catch (IOException exception) { journal.LastError = exception.Message; break; }
                catch (UnauthorizedAccessException exception) { journal.LastError = exception.Message; break; }
            }
            SaveCompletion(journal);
            foreach (FileStream stream in sourceLocks) stream.Dispose();
            sourceLocks.Clear();
            return await InspectAsync(journal, CancellationToken.None).ConfigureAwait(false);
        }
        finally { foreach (FileStream stream in sourceLocks) stream.Dispose(); }
    }

    public async Task<CleanupResult> ReadRecoveryAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        ValidateRoots();
        using WorkspaceLease lease = WorkspaceLease.Acquire(_databasePath, _cacheRoot);
        return await InspectAsync(ReadJournal(operationId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<CleanupResult> UndoAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Recoverable cleanup requires Windows handle-based rename.");
        ValidateRoots();
        using WorkspaceLease lease = WorkspaceLease.Acquire(_databasePath, _cacheRoot);
        Journal journal = ReadJournal(operationId);
        IndexRefreshGuard.Mark(_databasePath, _libraryRoots);
        journal.Cancelled = false;
        foreach (CleanupMove move in journal.Moves.Reverse())
        {
            if (cancellationToken.IsCancellationRequested) { journal.Cancelled = true; break; }
            ValidateMove(operationId, move);
            if (!File.Exists(move.ArchivePath)) continue;
            try
            {
                if (Path.Exists(move.OriginalPath)) throw new IOException($"Restore would overwrite an existing source: {move.OriginalPath}");
                using FileStream fileLock = await LockVerifiedAsync(move.ArchivePath, move.Size, move.LastWriteTimeUtc, move.Sha256, true, cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(move.OriginalPath)!);
                ManagedPaths.RejectLinks(move.OriginalPath);
                WindowsFileRename.Move(fileLock.SafeFileHandle, move.OriginalPath);
                journal.Changed = true;
                journal.RestoredPaths = [.. journal.RestoredPaths, move.OriginalPath];
                SaveJournal(journal, create: false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { journal.Cancelled = true; break; }
            catch (IOException exception) { journal.LastError = exception.Message; }
            catch (UnauthorizedAccessException exception) { journal.LastError = exception.Message; }
        }
        SaveCompletion(journal);
        return await InspectAsync(journal, CancellationToken.None).ConfigureAwait(false);
    }

    public IReadOnlyList<Guid> ListRecoveryOperations()
    {
        ValidateRoots();
        if (!Directory.Exists(_archiveRoot)) return [];
        List<Guid> operations = [];
        foreach (string directory in Directory.EnumerateDirectories(_archiveRoot))
        {
            ManagedPaths.RejectLinks(directory);
            if (Guid.TryParseExact(Path.GetFileName(directory), "N", out Guid id) && File.Exists(JournalPath(id))) operations.Add(id);
        }
        return operations.AsReadOnly();
    }

    private void ValidateRoots()
    {
        foreach (string root in _libraryRoots) ManagedPaths.ValidateTarget(root, []);
        ManagedPaths.ValidateTarget(_archiveRoot, [.. _libraryRoots, _cacheRoot, Path.GetDirectoryName(_databasePath)!]);
        ManagedPaths.RejectLinks(_databasePath);
        ManagedPaths.RejectLinks(_cacheRoot);
    }

    private string ValidateSource(string path)
    {
        string source = ManagedPaths.Normalize(path);
        ManagedPaths.RejectLinks(source);
        if (!_libraryRoots.Any(root => ManagedPaths.Contains(root, source) && !string.Equals(root, source, StringComparison.OrdinalIgnoreCase)) ||
            ManagedPaths.Contains(_cacheRoot, source) || ManagedPaths.Contains(Path.GetDirectoryName(_databasePath)!, source) || Directory.Exists(source))
            throw new IOException($"Unsafe cleanup source: {source}");
        return source;
    }

    private void ValidateMove(Guid id, CleanupMove move)
    {
        if (ValidateSource(move.OriginalPath) != move.OriginalPath || ManagedPaths.Normalize(move.ArchivePath) != move.ArchivePath ||
            !ManagedPaths.Contains(Path.Combine(OperationDirectory(id), "files"), move.ArchivePath) ||
            !string.Equals(Path.GetPathRoot(move.OriginalPath), Path.GetPathRoot(move.ArchivePath), StringComparison.OrdinalIgnoreCase) ||
            move.Size < 0 || move.Sha256.Length != 64 || !move.Sha256.All(Uri.IsHexDigit)) throw new IOException("Invalid cleanup journal mapping.");
        ManagedPaths.RejectLinks(move.ArchivePath);
    }

    private static async Task<CleanupKeeper> SnapshotAsync(string path, CancellationToken token)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        return new(path, stream.Length, File.GetLastWriteTimeUtc(stream.SafeFileHandle), hash);
    }

    private static async Task<FileStream> LockVerifiedAsync(string path, long size, DateTime modified, string hash, bool canRename, CancellationToken token)
    {
        ManagedPaths.RejectLinks(path);
        FileStream stream = canRename ? WindowsFileRename.Open(path) : new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != size || File.GetLastWriteTimeUtc(stream.SafeFileHandle) != modified ||
                Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != hash)
                throw new IOException($"File changed since preview: {path}");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private async Task<CleanupResult> InspectAsync(Journal journal, CancellationToken token)
    {
        List<CleanupEntryResult> entries = [];
        foreach (CleanupMove move in journal.Moves)
        {
            ValidateMove(journal.OperationId, move);
            bool original = Path.Exists(move.OriginalPath), archived = Path.Exists(move.ArchivePath);
            CleanupEntryState state = CleanupEntryState.Conflict;
            string? error = null;
            try
            {
                if (original == archived) throw new IOException("Both locations exist or both are missing; manual recovery is required.");
                using FileStream fileLock = await LockVerifiedAsync(original ? move.OriginalPath : move.ArchivePath,
                    move.Size, move.LastWriteTimeUtc, move.Sha256, false, token).ConfigureAwait(false);
                state = archived ? CleanupEntryState.Archived : journal.RestoredPaths.Contains(move.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    ? CleanupEntryState.Restored : CleanupEntryState.OriginalPresent;
            }
            catch (IOException exception) { error = exception.Message; }
            catch (UnauthorizedAccessException exception) { error = exception.Message; }
            entries.Add(new(move, state, error ?? journal.LastError));
        }
        return new(journal.OperationId, JournalPath(journal.OperationId), entries.AsReadOnly(), journal.Cancelled,
            journal.Changed || entries.Any(entry => entry.State is CleanupEntryState.Archived or CleanupEntryState.Conflict));
    }

    private Journal ReadJournal(Guid id)
    {
        ManagedPaths.RejectLinks(JournalPath(id));
        Journal journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(JournalPath(id))) ?? throw new IOException("Empty cleanup journal.");
        if (journal.OperationId != id || journal.DatabasePath != _databasePath || journal.CacheRoot != _cacheRoot || journal.ArchiveRoot != _archiveRoot ||
            !journal.LibraryRoots.SequenceEqual(_libraryRoots, StringComparer.OrdinalIgnoreCase) || journal.Moves.Length == 0 || journal.Keepers.Length == 0 ||
            journal.ManifestSha256 != ManifestHash(journal) ||
            journal.Moves.Select(move => move.OriginalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Moves.Length ||
            journal.Moves.Select(move => move.ArchivePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Moves.Length ||
            journal.Keepers.Any(keeper => journal.Moves.Any(move => string.Equals(move.OriginalPath, keeper.Path, StringComparison.OrdinalIgnoreCase))))
            throw new IOException("Cleanup journal does not match this workspace.");
        for (int index = 0; index < journal.Moves.Length; index++)
        {
            CleanupMove move = journal.Moves[index];
            ValidateMove(id, move);
            string expectedArchive = Path.Combine(OperationDirectory(id), "files", index.ToString(System.Globalization.CultureInfo.InvariantCulture), Path.GetFileName(move.OriginalPath));
            if (!string.Equals(expectedArchive, move.ArchivePath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Cleanup journal archive filename or ordinal was changed.");
        }
        foreach (CleanupKeeper keeper in journal.Keepers)
            if (ValidateSource(keeper.Path) != keeper.Path || keeper.Size < 0 || keeper.Sha256.Length != 64 || !keeper.Sha256.All(Uri.IsHexDigit))
                throw new IOException("Cleanup journal keeper was changed.");
        return journal;
    }

    private void SaveJournal(Journal journal, bool create)
    {
        journal.ManifestSha256 = ManifestHash(journal);
        string path = JournalPath(journal.OperationId);
        ManagedPaths.RejectLinks(path);
        Directory.CreateDirectory(OperationDirectory(journal.OperationId));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, journal);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: !create);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void SaveCompletion(Journal journal)
    {
        try { SaveJournal(journal, create: false); }
        catch (IOException exception) { journal.LastError = exception.Message; }
        catch (UnauthorizedAccessException exception) { journal.LastError = exception.Message; }
    }


    private string OperationDirectory(Guid id) => Path.Combine(_archiveRoot, id.ToString("N"));
    private string JournalPath(Guid id) => Path.Combine(OperationDirectory(id), "cleanup.json");
    private static string ManifestHash(Journal journal) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { journal.OperationId, journal.DatabasePath, journal.CacheRoot, journal.LibraryRoots,
            journal.ArchiveRoot, journal.Keepers, journal.Moves })));

    private sealed class IssuedPlan(Guid id, DateTimeOffset expiresAt, CleanupKeeper[] keepers, CleanupMove[] moves) : SmartCleanupPlan
    {
        public override Guid OperationId { get; } = id;
        public override DateTimeOffset ExpiresAt { get; } = expiresAt;
        public override IReadOnlyList<CleanupKeeper> Keepers { get; } = Array.AsReadOnly(keepers);
        public override IReadOnlyList<CleanupMove> Moves { get; } = Array.AsReadOnly(moves);
    }

    private sealed class Journal
    {
        public Guid OperationId { get; set; }
        public string DatabasePath { get; set; } = "";
        public string CacheRoot { get; set; } = "";
        public string ArchiveRoot { get; set; } = "";
        public string[] LibraryRoots { get; set; } = [];
        public CleanupKeeper[] Keepers { get; set; } = [];
        public CleanupMove[] Moves { get; set; } = [];
        public string[] RestoredPaths { get; set; } = [];
        public bool Changed { get; set; }
        public bool Cancelled { get; set; }
        public string? LastError { get; set; }
        public string ManifestSha256 { get; set; } = "";
    }
}
