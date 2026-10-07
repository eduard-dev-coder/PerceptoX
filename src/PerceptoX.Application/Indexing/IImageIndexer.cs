namespace PerceptoX.Application.Indexing;

public interface IImageIndexer
{
    Task<IndexingResult> IndexAsync(IndexingRequest request, CancellationToken cancellationToken = default);
}
