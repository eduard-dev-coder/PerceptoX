using System.Diagnostics;
using System.Security.Cryptography;
using PerceptoX.Application.Imaging;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Infrastructure.Persistence;
using PerceptoX.Presentation.Services;
using PerceptoX.WinUI.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.Infrastructure.Tests;

[Collection("Optional ImageMagick runtime")]
public sealed class OptionalMagickDecoderTests
{
    internal const string InstalledExecutable = @"C:\Program Files\ImageMagick-7.1.2-Q16-HDRI\magick.exe";
    internal static readonly string BundledExecutable = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../vendor/imagemagick-x64/magick.exe"));

    [BundledMagickFact]
    public async Task AppLocalRestrictedBundleDecodesAvifWithoutInstalledRuntimeLookup()
    {
        using TestDirectory directory = new();
        // Encoding is fixture preparation only; decoding must use the app-local bundle.
        string avif = await CreateSyntheticAvifAsync(directory.Path);
        byte[] original = File.ReadAllBytes(avif);
        OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(BundledExecutable);
        Assert.True(decoder.SupportsExtension(".heic"));
        Assert.True(decoder.SupportsExtension(".heif"));
        Assert.True(decoder.SupportsExtension(".avif"));
        using OptionalMagickDecodedImage decoded = await decoder.DecodeToTemporaryPngAsync(avif);
        Assert.Equal(48, decoded.OriginalWidth);
        Assert.Equal(96, decoded.OriginalHeight);
        Assert.Equal(original, File.ReadAllBytes(avif));
        using Image<Rgba32> image = Image.Load<Rgba32>(decoded.PngPath);
        Assert.Equal(48, image.Width);
        Assert.Equal(96, image.Height);
        using ImageSharpImageProcessor processor = new(modernDecoder: decoder);
        ImageProcessingRequest request = new(avif, 1, "bundled-avif-v1", Path.Combine(directory.Path, "thumbs"));
        var fingerprints = await processor.ProcessAsync(request);
        Assert.NotNull(fingerprints);
    }

    [Theory]
    [InlineData("magick.exe")]
    [InlineData(@".\magick.exe")]
    [InlineData(@"..\magick.exe")]
    public async Task RuntimeSelectionRejectsRelativePathsWithoutSearchingPath(string executable)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => OptionalMagickDecoder.CreateAsync(executable));
    }

    [Fact]
    public async Task RuntimeSelectionRejectsMissingExplicitExecutable()
    {
        using TestDirectory directory = new();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            OptionalMagickDecoder.CreateAsync(Path.Combine(directory.Path, "missing-magick.exe")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [InstalledMagickFact]
    public async Task SelectedRuntimeReportsReadableFormatsAndStableProfile()
    {
        OptionalMagickDecoder first = await OptionalMagickDecoder.CreateAsync(InstalledExecutable);
        OptionalMagickDecoder repeated = await OptionalMagickDecoder.CreateAsync(InstalledExecutable);

        Assert.StartsWith("Version: ImageMagick 7.", first.RuntimeVersion, StringComparison.Ordinal);
        Assert.Matches("^optional-magick-sha256-[0-9A-F]{64}$", first.RuntimeProfileId);
        Assert.Equal(first.RuntimeProfileId, repeated.RuntimeProfileId);
        Assert.True(first.SupportsExtension(".avif"));
        Assert.True(first.SupportsExtension(".AVIF"));
        Assert.True(first.SupportsExtension(".heic"));
        Assert.True(first.SupportsExtension(".heif"));
        Assert.False(first.SupportsExtension(".png"));
        Assert.False(first.SupportsExtension("avif"));
    }

    [InstalledMagickFact]
    public async Task SyntheticAvifProducesCanonicalPortraitPngAndDisposalRemovesOnlyItsOutput()
    {
        using TestDirectory directory = new();
        string avif = await CreateSyntheticAvifAsync(directory.Path);
        byte[] original = File.ReadAllBytes(avif);
        DateTime modified = File.GetLastWriteTimeUtc(avif);
        OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(InstalledExecutable);
        OptionalMagickDecodedImage decoded = await decoder.DecodeToTemporaryPngAsync(avif);
        string png = decoded.PngPath;
        string outputDirectory = Path.GetDirectoryName(png)!;
        try
        {
            Assert.Equal(48, decoded.OriginalWidth);
            Assert.Equal(96, decoded.OriginalHeight);
            Assert.True(Path.IsPathFullyQualified(png));
            Assert.Equal("canonical.png", Path.GetFileName(png));
            Assert.StartsWith("PerceptoX-OptionalCodec-", Path.GetFileName(outputDirectory), StringComparison.Ordinal);
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                Path.GetDirectoryName(outputDirectory));
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, File.ReadAllBytes(png)[..8]);
            using Image<Rgba32> image = Image.Load<Rgba32>(png);
            Assert.Equal(48, image.Width);
            Assert.Equal(96, image.Height);
            Assert.Single(image.Frames);
            Assert.Null(image.Metadata.ExifProfile);
            Assert.Null(image.Metadata.IccProfile);
            Assert.Null(image.Metadata.XmpProfile);
            Assert.True(image[24, 8].R > image[24, 88].R);
            Assert.Equal(original, File.ReadAllBytes(avif));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(avif));
        }
        finally { decoded.Dispose(); }

        decoded.Dispose();
        Assert.False(File.Exists(png));
        Assert.False(Directory.Exists(outputDirectory));
        Assert.Equal(original, File.ReadAllBytes(avif));
        Assert.True(File.Exists(Path.Combine(directory.Path, "synthetic.png")));
    }

    [InstalledMagickFact]
    public async Task CancelledDecodeLeavesOriginalAndNoNewTemporaryOutput()
    {
        using TestDirectory directory = new();
        string avif = await CreateSyntheticAvifAsync(directory.Path);
        byte[] original = SHA256.HashData(File.ReadAllBytes(avif));
        OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(InstalledExecutable);
        HashSet<string> before = OwnedCodecDirectories();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            decoder.DecodeToTemporaryPngAsync(avif, cancellation.Token));

        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(avif)));
        Assert.Empty(OwnedCodecDirectories().Except(before));
    }

    [InstalledMagickFact]
    public async Task CorruptAvifIsRejectedWithoutPublishingPngOrChangingOriginal()
    {
        using TestDirectory directory = new();
        string avif = Path.Combine(directory.Path, "corrupt.avif");
        byte[] original = [0, 1, 2, 3, 4, 5];
        File.WriteAllBytes(avif, original);
        OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(InstalledExecutable);
        HashSet<string> before = OwnedCodecDirectories();

        await Assert.ThrowsAsync<InvalidDataException>(() => decoder.DecodeToTemporaryPngAsync(avif));

        Assert.Equal(original, File.ReadAllBytes(avif));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.png"));
        Assert.Empty(OwnedCodecDirectories().Except(before));
    }

    [InstalledMagickFact]
    public async Task UnsupportedSourceExtensionIsRejectedBeforeOutput()
    {
        using TestDirectory directory = new();
        OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(InstalledExecutable);
        HashSet<string> before = OwnedCodecDirectories();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            decoder.DecodeToTemporaryPngAsync(Path.Combine(directory.Path, "missing.png")));

        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
        Assert.Empty(OwnedCodecDirectories().Except(before));
    }

    [InstalledMagickFact]
    public async Task OptionalProcessorIndexesAvifAndRegeneratesThumbnailWithMatchingVersion()
    {
        using TestDirectory directory = new();
        string avif = await CreateSyntheticAvifAsync(directory.Path);
        byte[] original = File.ReadAllBytes(avif);
        OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(InstalledExecutable);
        using ImageSharpImageProcessor standard = new();
        using ImageSharpImageProcessor optional = new(modernDecoder: decoder);
        using ImageSharpImageProcessor repeated = new(modernDecoder: decoder);
        Assert.NotEqual(standard.ProcessingProfileId, optional.ProcessingProfileId);
        Assert.Equal(optional.ProcessingProfileId, repeated.ProcessingProfileId);
        ImageProcessingRequest request = new(avif, 1, "synthetic-avif-v1", Path.Combine(directory.Path, "thumbs"));
        await Assert.ThrowsAsync<UnknownImageFormatException>(() => standard.ProcessAsync(request));
        HashSet<string> before = OwnedCodecDirectories();

        ImageProcessingResult result = await optional.ProcessAsync(request);

        Assert.Equal(48, result.Fingerprints.Width);
        Assert.Equal(96, result.Fingerprints.Height);
        Assert.Equal(3, result.Fingerprints.Fingerprints.Count);
        Assert.Equal(8, result.Fingerprints.GetRequired("phash").Value.Bytes.Length);
        Assert.Equal(8, result.Fingerprints.GetRequired("dhash").Value.Bytes.Length);
        Assert.Equal(72, result.Fingerprints.GetRequired("multiregion").Value.Bytes.Length);
        Assert.Equal(request.SourceVersion, result.Thumbnail.SourceVersion);
        string thumbnail = Path.Combine(request.CacheRoot,
            result.Thumbnail.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(thumbnail));
        byte[] firstThumbnail = File.ReadAllBytes(thumbnail);
        File.Delete(thumbnail);

        var regenerated = await optional.RegenerateThumbnailAsync(request);

        Assert.Equal(result.Thumbnail.SourceVersion, regenerated.SourceVersion);
        Assert.Equal(result.Thumbnail.RelativePath, regenerated.RelativePath);
        Assert.Equal(firstThumbnail, File.ReadAllBytes(thumbnail));
        using Image<Rgba32> image = Image.Load<Rgba32>(thumbnail);
        Assert.Equal(48, image.Width);
        Assert.Equal(96, image.Height);
        Assert.Null(image.Metadata.ExifProfile);
        Assert.Null(image.Metadata.IccProfile);
        Assert.Equal(original, File.ReadAllBytes(avif));
        Assert.Empty(OwnedCodecDirectories().Except(before));
    }

    [InstalledMagickFact]
    public async Task OptionalDesktopWorkflowDiscoversAvifAndStoresOriginalPathWithReusableFingerprints()
    {
        using TestDirectory directory = new();
        string library = Path.Combine(directory.Path, "library");
        Directory.CreateDirectory(library);
        string avif = await CreateSyntheticAvifAsync(directory.Path);
        string libraryAvif = Path.Combine(library, Path.GetFileName(avif));
        File.Move(avif, libraryAvif);
        byte[] original = File.ReadAllBytes(libraryAvif);
        OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(InstalledExecutable);
        DesktopPerceptoXWorkflow workflow = new(modernDecoder: decoder);
        WorkspaceConfiguration workspace = new(library,
            Path.Combine(directory.Path, "index.db"), Path.Combine(directory.Path, "thumbs"));

        IndexingSummary initial = await workflow.IndexAsync(workspace, CancellationToken.None);

        Assert.Equal(1, initial.Discovered);
        Assert.Equal(1, initial.Processed);
        Assert.Equal(0, initial.Failed);
        Assert.True(initial.DiscoveryComplete);
        SqliteIndexReader reader = new(workspace.DatabasePath);
        var record = reader.FindByPath(library, libraryAvif);
        Assert.NotNull(record);
        Assert.Equal(libraryAvif, record.FilePath);
        Assert.Equal(48, record.Width);
        Assert.Equal(96, record.Height);
        Assert.Equal(3, reader.CountFingerprints(record.Id));
        Assert.NotNull(reader.FindThumbnail(record.Id));
        using ImageSharpImageProcessor processor = new(modernDecoder: decoder);
        Assert.Equal(1, reader.LoadSnapshot(library, processor.ProcessingProfileId).Count);

        IndexingSummary repeated = await workflow.IndexAsync(workspace, CancellationToken.None);

        Assert.Equal(0, repeated.Processed);
        Assert.Equal(1, repeated.SkippedUnchanged);
        Assert.Equal(0, repeated.Failed);
        Assert.Equal(original, File.ReadAllBytes(libraryAvif));
    }

    private static async Task<string> CreateSyntheticAvifAsync(string directory)
    {
        string png = Path.Combine(directory, "synthetic.png");
        string avif = Path.Combine(directory, "synthetic portrait [local].avif");
        using (Image<Rgba32> image = new(48, 96))
        {
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                image[x, y] = new Rgba32((byte)(240 - y * 2), (byte)(x * 4), (byte)(y * 2));
            image.SaveAsPng(png);
        }

        ProcessStartInfo start = new(InstalledExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = directory
        };
        foreach (string argument in new[] { "-limit", "thread", "2", png, "-quality", "95", "AVIF:" + avif })
            start.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = start };
        Assert.True(process.Start());
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await stdout;
            string error = await stderr;
            Assert.True(process.ExitCode == 0, "Synthetic AVIF generation failed: " + error);
            Assert.True(File.Exists(avif));
            return avif;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static HashSet<string> OwnedCodecDirectories() =>
        Directory.EnumerateDirectories(Path.GetTempPath(), "PerceptoX-OptionalCodec-*")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private sealed class TestDirectory : IDisposable
    {
        private readonly string _parent = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, ".optional-codec-test-artifacts"));

        public TestDirectory()
        {
            Path = System.IO.Path.Combine(_parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            string resolved = System.IO.Path.GetFullPath(Path);
            if (System.IO.Path.GetDirectoryName(resolved) != _parent ||
                !Guid.TryParseExact(System.IO.Path.GetFileName(resolved), "N", out _))
                throw new IOException("Test cleanup escaped its unique fixture directory.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}

public sealed class InstalledMagickFactAttribute : FactAttribute
{
    public InstalledMagickFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(OptionalMagickDecoderTests.InstalledExecutable))
            Skip = "Optional ImageMagick integration requires the explicitly selected local ImageMagick 7 runtime; it is not bundled.";
    }
}

public sealed class BundledMagickFactAttribute : FactAttribute
{
    public BundledMagickFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(OptionalMagickDecoderTests.InstalledExecutable) ||
            !File.Exists(OptionalMagickDecoderTests.BundledExecutable))
            Skip = "Prepare the pinned bundle and provide the explicit fixture encoder before this integration test.";
    }
}

[CollectionDefinition("Optional ImageMagick runtime", DisableParallelization = true)]
public sealed class OptionalMagickRuntimeTestGroup;
