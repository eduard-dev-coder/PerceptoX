namespace PerceptoX.Core.Fingerprints;

public sealed record FingerprintDescriptor
{
    public FingerprintDescriptor(
        string algorithmId,
        int algorithmVersion,
        string profileId,
        int bitLength,
        int sampleWidth,
        int sampleHeight)
    {
        if (!IsValidAlgorithmId(algorithmId))
        {
            throw new ArgumentException("Use a lowercase ASCII algorithm ID of at most 64 characters.", nameof(algorithmId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(algorithmVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleHeight);

        AlgorithmId = algorithmId;
        AlgorithmVersion = algorithmVersion;
        ProfileId = profileId;
        BitLength = bitLength;
        SampleWidth = sampleWidth;
        SampleHeight = sampleHeight;
    }

    public string AlgorithmId { get; }
    public int AlgorithmVersion { get; }
    public string ProfileId { get; }
    public int BitLength { get; }
    public int SampleWidth { get; }
    public int SampleHeight { get; }

    private static bool IsValidAlgorithmId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiLowerOrDigit(value[0]))
        {
            return false;
        }

        foreach (char character in value.AsSpan(1))
        {
            if (!IsAsciiLowerOrDigit(character) && character is not ('.' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLowerOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}
