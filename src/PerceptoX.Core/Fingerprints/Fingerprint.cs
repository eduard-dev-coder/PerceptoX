namespace PerceptoX.Core.Fingerprints;

public sealed record Fingerprint
{
    public Fingerprint(FingerprintDescriptor descriptor, FingerprintValue value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(value);

        if (descriptor.BitLength != value.BitLength)
        {
            throw new ArgumentException("Fingerprint value length does not match its descriptor.", nameof(value));
        }

        Descriptor = descriptor;
        Value = value;
    }

    public FingerprintDescriptor Descriptor { get; }
    public FingerprintValue Value { get; }
}
