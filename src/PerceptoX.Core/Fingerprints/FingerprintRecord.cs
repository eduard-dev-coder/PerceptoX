namespace PerceptoX.Core.Fingerprints;

public sealed record FingerprintRecord
{
    public FingerprintRecord(long imageId, Fingerprint fingerprint)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentNullException.ThrowIfNull(fingerprint);

        ImageId = imageId;
        Fingerprint = fingerprint;
    }

    public long ImageId { get; }
    public Fingerprint Fingerprint { get; }
    public FingerprintDescriptor Descriptor => Fingerprint.Descriptor;
    public FingerprintValue Value => Fingerprint.Value;
}
