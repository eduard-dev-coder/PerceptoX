namespace PerceptoX.Application.Cleanup;

public enum KeeperRule { HighestResolution, Newest }
public sealed record CleanupFileCandidate(string Path, int Width, int Height, DateTime LastWriteTimeUtc);
public sealed record VerifiedCleanupGroup(IReadOnlyList<CleanupFileCandidate> Files, bool ManuallyVerified);
public sealed record CleanupMove(string OriginalPath, string ArchivePath, long Size, DateTime LastWriteTimeUtc, string Sha256);
public sealed record CleanupKeeper(string Path, long Size, DateTime LastWriteTimeUtc, string Sha256);

/// <summary>Immutable service preview; the caller must obtain explicit UI confirmation before execution.</summary>
public abstract class SmartCleanupPlan
{
    public abstract Guid OperationId { get; }
    public abstract DateTimeOffset ExpiresAt { get; }
    public abstract IReadOnlyList<CleanupKeeper> Keepers { get; }
    public abstract IReadOnlyList<CleanupMove> Moves { get; }
}

public enum CleanupEntryState { OriginalPresent, Archived, Restored, Conflict }
public sealed record CleanupEntryResult(CleanupMove Move, CleanupEntryState State, string? Error);
public sealed record CleanupResult(Guid OperationId, string JournalPath, IReadOnlyList<CleanupEntryResult> Entries,
    bool Cancelled, bool IndexRequiresRefresh);

public interface ISmartCleanupService
{
    Task<SmartCleanupPlan> PreviewAsync(IReadOnlyList<VerifiedCleanupGroup> groups, KeeperRule rule, CancellationToken cancellationToken = default);
    Task<CleanupResult> ExecuteAsync(SmartCleanupPlan plan, IProgress<CleanupEntryResult>? progress = null,
        CancellationToken cancellationToken = default);
    Task<CleanupResult> ReadRecoveryAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<CleanupResult> UndoAsync(Guid operationId, CancellationToken cancellationToken = default);
    IReadOnlyList<Guid> ListRecoveryOperations();
}
