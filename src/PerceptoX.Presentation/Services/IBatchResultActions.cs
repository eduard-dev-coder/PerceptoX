namespace PerceptoX.Presentation.Services;

public sealed record CopyOutcome(int Copied, int Failed, bool Cancelled, IReadOnlyList<string> Errors);

public interface IBatchResultActions
{
    Task<CopyOutcome> CopyAsync(IReadOnlyList<string> sourcePaths, string destination,
        CancellationToken cancellationToken);
    Task<string> ExportAsync(BatchMatchSummary summary, string destination, bool html,
        CancellationToken cancellationToken);
}
