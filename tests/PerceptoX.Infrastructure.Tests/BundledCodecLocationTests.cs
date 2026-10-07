using PerceptoX.Infrastructure.Imaging;

namespace PerceptoX.Infrastructure.Tests;

public sealed class BundledCodecLocationTests
{
    [Fact]
    public void RelativeApplicationDirectoryIsRejected() =>
        Assert.Throws<ArgumentException>(() => BundledCodecLocation.Find("relative"));

    [Fact]
    public void MissingBundleDoesNotSearchTheMachine() =>
        Assert.Null(BundledCodecLocation.Find(Path.Combine(AppContext.BaseDirectory, "missing-bundle-" + Guid.NewGuid().ToString("N"))));

    [Fact]
    public void BundleIsLocatedOnlyUnderTheApplicationDirectory()
    {
        string parent = Path.Combine(AppContext.BaseDirectory, ".bundle-location-tests");
        string directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        string runtime = Path.Combine(directory, "codecs", "imagemagick");
        Directory.CreateDirectory(runtime);
        try
        {
            string executable = Path.Combine(runtime, "magick.exe");
            File.WriteAllBytes(executable, [0]);
            Assert.Equal(executable, BundledCodecLocation.Find(directory));
        }
        finally
        {
            Assert.Equal(parent, Path.GetDirectoryName(directory));
            Assert.True(Guid.TryParseExact(Path.GetFileName(directory), "N", out _));
            Directory.Delete(directory, recursive: true);
        }
    }
}
