using PerceptoX.Core.Fingerprints;
using PerceptoX.Core.Images;

namespace PerceptoX.Core.Tests;

public sealed class RecordTests
{
    [Fact]
    public void ImageRecordRetainsMetadataWithoutHashOrThumbnailBytes()
    {
        string path = Path.Combine(Path.GetTempPath(), "sample.png");
        ImageRecord record = new(42, 3, "v1:sample.png", path, 192, 128, 1024, DateTimeOffset.UnixEpoch);

        Assert.Equal("sample.png", record.FileName);
        Assert.Equal(1.5, record.AspectRatio);
        Assert.True(record.IsActive);
    }

    [Fact]
    public void ImageRecordRejectsInvalidIdentityDimensionsAndTimestamp()
    {
        string path = Path.Combine(Path.GetTempPath(), "sample.png");
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageRecord(0, 1, "key", path, 1, 1, 0, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageRecord(1, 0, "key", path, 1, 1, 0, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageRecord(1, 1, "key", path, 0, 1, 0, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageRecord(1, 1, "key", path, 1, 1, -1, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new ImageRecord(1, 1, "key", "relative.png", 1, 1, 0, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new ImageRecord(1, 1, "key", path, 1, 1, 0, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(1))));
    }

    [Fact]
    public void FingerprintRecordsAllowIdenticalHashesForDifferentImages()
    {
        Fingerprint fingerprint = new(new FingerprintDescriptor("test", 1, "profile", 64, 1, 1), FingerprintValue.FromUInt64(0));
        FingerprintRecord first = new(1, fingerprint);
        FingerprintRecord second = new(2, fingerprint);

        Assert.Equal(first.Value, second.Value);
        Assert.NotEqual(first.ImageId, second.ImageId);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FingerprintRecord(0, fingerprint));
    }

    [Theory]
    [InlineData("../escape.jpg")]
    [InlineData("a/../escape.jpg")]
    [InlineData("/absolute.jpg")]
    [InlineData("C:\\absolute.jpg")]
    [InlineData("a//double.jpg")]
    [InlineData("a/invalid?.jpg")]
    public void ThumbnailRecordRejectsUnsafeCachePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => new ThumbnailRecord(1, "source-v1", "jpeg256-v1", path, "jpeg", 128, 64));
    }

    [Fact]
    public void ThumbnailRecordRetainsCacheIdentity()
    {
        ThumbnailRecord record = new(42, "source-v1", "jpeg256-v1", "2a/42-source-v1.jpg", "jpeg", 128, 64);

        Assert.Equal(42, record.ImageId);
        Assert.Equal("jpeg256-v1", record.ProfileId);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThumbnailRecord(42, "source-v1", "jpeg256-v1", "a.jpg", "jpeg", 0, 1));
    }
}
