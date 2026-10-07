using PerceptoX.Core.Fingerprints;
using System.Collections.ObjectModel;

namespace PerceptoX.Application.Matching;

public sealed class ImageFingerprintSet
{
    private readonly ReadOnlyDictionary<string, Fingerprint> _fingerprints;

    public ImageFingerprintSet(int width, int height, IEnumerable<Fingerprint> fingerprints)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(fingerprints);

        Dictionary<string, Fingerprint> values = new(StringComparer.Ordinal);
        foreach (Fingerprint fingerprint in fingerprints)
        {
            ArgumentNullException.ThrowIfNull(fingerprint);
            if (!values.TryAdd(fingerprint.Descriptor.AlgorithmId, fingerprint))
            {
                throw new ArgumentException("Only one active profile per algorithm is allowed in a set.", nameof(fingerprints));
            }
        }

        if (values.Count == 0)
        {
            throw new ArgumentException("At least one fingerprint is required.", nameof(fingerprints));
        }

        Width = width;
        Height = height;
        _fingerprints = new ReadOnlyDictionary<string, Fingerprint>(values);
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyDictionary<string, Fingerprint> Fingerprints => _fingerprints;

    public Fingerprint GetRequired(string algorithmId) =>
        _fingerprints.TryGetValue(algorithmId, out Fingerprint? fingerprint)
            ? fingerprint
            : throw new KeyNotFoundException($"The '{algorithmId}' fingerprint is missing.");
}
