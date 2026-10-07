namespace PerceptoX.Application.Imaging;

public interface IImageProcessor
{
    Task<ImageProcessingResult> ProcessAsync(ImageProcessingRequest request, CancellationToken cancellationToken = default);
}
