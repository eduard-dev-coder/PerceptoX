using PerceptoX.Application.Imaging;

namespace PerceptoX.Infrastructure.Indexing;

internal sealed record ProcessedFile(PreparedFile Prepared, ImageProcessingResult? Result, string? ErrorCode)
{
    public static ProcessedFile Success(PreparedFile prepared, ImageProcessingResult result) =>
        new(prepared, result, null);

    public static ProcessedFile Failure(PreparedFile prepared, string errorCode) =>
        new(prepared, null, errorCode);
}
