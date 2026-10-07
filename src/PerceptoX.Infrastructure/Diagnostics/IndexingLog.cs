using Microsoft.Extensions.Logging;

namespace PerceptoX.Infrastructure.Diagnostics;

internal static partial class IndexingLog
{
    [LoggerMessage(1, LogLevel.Information, "Index scan {ScanId} started for {Library} with {WorkerCount} workers")]
    internal static partial void Started(ILogger logger, long scanId, string library, int workerCount);
    [LoggerMessage(2, LogLevel.Warning, "Progress observer failed with {ErrorCode}")]
    internal static partial void ObserverFailed(ILogger logger, string errorCode);
    [LoggerMessage(3, LogLevel.Warning, "Scan {ScanId} skipped {RelativePath} with {ErrorCode}")]
    internal static partial void Skipped(ILogger logger, long scanId, string relativePath, string errorCode);
    [LoggerMessage(4, LogLevel.Warning, "Scan {ScanId} failed to process {RelativePath} with {ErrorCode}")]
    internal static partial void Failed(ILogger logger, long scanId, string relativePath, string errorCode);
    [LoggerMessage(5, LogLevel.Information, "Scan {ScanId} completed in {ElapsedMs} ms: processed {Processed}, failed {Failed}, unsupported {Unsupported}, inaccessible {Inaccessible}, rate {ImagesPerSecond}")]
    internal static partial void Completed(ILogger logger, long scanId, double elapsedMs, int processed, int failed,
        int unsupported, int inaccessible, double imagesPerSecond);
    [LoggerMessage(6, LogLevel.Warning, "Scan {ScanId} aborted after {ElapsedMs} ms")]
    internal static partial void Aborted(ILogger logger, long scanId, double elapsedMs);
}
