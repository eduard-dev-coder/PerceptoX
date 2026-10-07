namespace PerceptoX.Application.Maintenance;

public enum MaintenanceLevel { Cache, IndexAndCache, Reports }
public sealed record MaintenanceRequest(string DatabasePath, string CacheRoot,
    IReadOnlyList<string> ProtectedRoots, MaintenanceLevel Level, string? ReportRoot = null)
{
    public IProgress<MaintenanceProgress>? Progress { get; init; }
}
public sealed record MaintenanceProgress(int Deleted, int Total, long FreedBytes);
public sealed record MaintenanceFile(string Path, long Length, long LastWriteUtcTicks);
public sealed record MaintenancePlan(Guid Id, MaintenanceRequest Request,
    IReadOnlyList<MaintenanceFile> Files, IReadOnlyList<string> Directories,
    IReadOnlyList<string> AffectedLibraries, DateTimeOffset CreatedUtc)
{
    public long Bytes => Files.Sum(file => file.Length);
    public int PreservedFiles { get; init; }
}
public sealed record MaintenanceResult(int Deleted, long FreedBytes, bool Cancelled,
    IReadOnlyList<string> Errors, bool IndexRemoved);
public sealed record StorageUsage(long DatabaseBytes, long CacheBytes, int CacheFiles);

public interface IMaintenanceService
{
    Task<StorageUsage> MeasureAsync(MaintenanceRequest request, CancellationToken cancellationToken);
    Task<MaintenancePlan> PreviewAsync(MaintenanceRequest request, CancellationToken cancellationToken);
    Task<MaintenanceResult> ExecuteAsync(MaintenancePlan confirmedPlan, CancellationToken cancellationToken);
}
