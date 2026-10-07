using Microsoft.Extensions.Logging;

namespace PerceptoX.Infrastructure.Diagnostics;

public static partial class WorkflowDiagnostics
{
    [LoggerMessage(30, LogLevel.Information, "Search {JobId}: query {QueryName}, results {Results}, elapsed {ElapsedMs} ms")]
    public static partial void SearchCompleted(ILogger logger, Guid jobId, string queryName, int results, double elapsedMs);
    [LoggerMessage(31, LogLevel.Information, "Batch {JobId}: queries {Queries}, found {Found}, ambiguous {Ambiguous}, errors {Errors}, elapsed {ElapsedMs} ms")]
    public static partial void BatchCompleted(ILogger logger, Guid jobId, int queries, int found, int ambiguous, int errors, double elapsedMs);
    [LoggerMessage(32, LogLevel.Warning, "Workflow {JobId} stopped with {ErrorCode} after {ElapsedMs} ms")]
    public static partial void Stopped(ILogger logger, Guid jobId, string errorCode, double elapsedMs);
}
