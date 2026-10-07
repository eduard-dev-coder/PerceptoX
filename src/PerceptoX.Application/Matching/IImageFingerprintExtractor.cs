namespace PerceptoX.Application.Matching;

public interface IImageFingerprintExtractor
{
    ImageFingerprintSet Extract(string path);
}
