namespace PerceptoX.Core.Fingerprints;

public interface IImageFingerprintAlgorithm
{
    FingerprintDescriptor Descriptor { get; }

    // The caller supplies one grayscale byte per pixel at the descriptor's dimensions.
    Fingerprint Compute(ReadOnlySpan<byte> grayscalePixels);
}
