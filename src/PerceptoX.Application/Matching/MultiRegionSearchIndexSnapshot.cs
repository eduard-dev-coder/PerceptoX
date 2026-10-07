using PerceptoX.Core.Fingerprints;

namespace PerceptoX.Application.Matching;

public sealed class MultiRegionSearchIndexSnapshot
{
    private readonly long[] _imageIds;
    private readonly byte[] _hashes;

    public MultiRegionSearchIndexSnapshot(
        long scanId,
        string processingProfileId,
        ReadOnlySpan<long> imageIds,
        ReadOnlySpan<byte> hashes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(processingProfileId);
        if (hashes.Length != checked(imageIds.Length * MultiRegionHash576.ByteLength))
        {
            throw new ArgumentException("The hash buffer length does not match the image count.", nameof(hashes));
        }

        _imageIds = imageIds.ToArray();
        _hashes = hashes.ToArray();
        for (int index = 0; index < _imageIds.Length; index++)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_imageIds[index], nameof(imageIds));
            if (index != 0 && _imageIds[index - 1] >= _imageIds[index])
            {
                throw new ArgumentException("Image IDs must be unique and strictly increasing.", nameof(imageIds));
            }
        }

        ScanId = scanId;
        ProcessingProfileId = processingProfileId;
    }

    public long ScanId { get; }
    public string ProcessingProfileId { get; }
    public int Count => _imageIds.Length;

    public long GetImageId(int index) => _imageIds[index];

    public ReadOnlySpan<byte> GetHash(int index) =>
        _hashes.AsSpan(checked(index * MultiRegionHash576.ByteLength), MultiRegionHash576.ByteLength);

    public bool TryGetHashById(long imageId, out ReadOnlySpan<byte> hash)
    {
        int index = Array.BinarySearch(_imageIds, imageId);
        if (index < 0)
        {
            hash = default;
            return false;
        }

        hash = GetHash(index);
        return true;
    }
}
