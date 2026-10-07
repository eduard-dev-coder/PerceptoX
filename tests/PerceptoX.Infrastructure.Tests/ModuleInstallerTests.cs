using System.Security.Cryptography;
using System.Text.Json;
using PerceptoX.Infrastructure.Modules;

namespace PerceptoX.Infrastructure.Tests;

public sealed class ModuleInstallerTests
{
    [Fact]
    public async Task MissingModuleDoesNotCreateDirectories()
    {
        using Fixture fixture = new(createPayload: false);
        ModuleInspection result = await new ModuleInstaller(fixture.Root).InspectAsync();
        Assert.False(result.PayloadAvailable);
        Assert.Null(result.ExecutablePath);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "modules")));
    }

    [Fact]
    public async Task NoConsentMakesNoChanges()
    {
        using Fixture fixture = new();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ModuleInstaller(fixture.Root).InstallAsync(false, Verified));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "modules")));
    }

    [Fact]
    public async Task TamperedPayloadIsRejectedBeforeCopy()
    {
        using Fixture fixture = new();
        File.AppendAllText(Path.Combine(fixture.Payload, "magick.exe"), "tamper");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModuleInstaller(fixture.Root).InstallAsync(true, Verified));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "modules")));
    }

    [Fact]
    public async Task InstallIsVisibleToNextInspectionAndPreservesEarlierSlots()
    {
        using Fixture fixture = new();
        ModuleInstaller installer = new(fixture.Root);
        Assert.Null((await installer.InspectAsync()).ExecutablePath);
        await installer.InstallAsync(true, Verified);
        string first = (await installer.InspectAsync()).ExecutablePath!;
        byte[] original = File.ReadAllBytes(first);
        await installer.InstallAsync(true, Verified);
        string second = (await installer.InspectAsync()).ExecutablePath!;
        Assert.NotEqual(first, second);
        Assert.Equal(original, File.ReadAllBytes(first));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Root, "modules", "installed"), "stage-*"));
    }

    [Fact]
    public async Task CancellationAfterCopyPreservesPreviousPointerAndRemovesStaging()
    {
        using Fixture fixture = new();
        ModuleInstaller installer = new(fixture.Root);
        await installer.InstallAsync(true, Verified);
        string first = (await installer.InspectAsync()).ExecutablePath!;
        using CancellationTokenSource cancellation = new();
        IProgress<ModuleInstallProgress> progress = new InlineProgress(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(true, Verified, progress, token: cancellation.Token));
        Assert.Equal(first, (await installer.InspectAsync()).ExecutablePath);
        Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, "modules", "installed"), "slot-*"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Root, "modules", "installed"), "stage-*"));
    }

    [Fact]
    public async Task RuntimeProbeFailurePreservesPreviousActivation()
    {
        using Fixture fixture = new();
        ModuleInstaller installer = new(fixture.Root);
        await installer.InstallAsync(true, Verified);
        string first = (await installer.InspectAsync()).ExecutablePath!;
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(true, (_, _) => throw new InvalidDataException("codec missing")));
        Assert.Equal(first, (await installer.InspectAsync()).ExecutablePath);
    }

    [Fact]
    public async Task InstalledFileTamperingIsReported()
    {
        using Fixture fixture = new();
        ModuleInstaller installer = new(fixture.Root);
        await installer.InstallAsync(true, Verified);
        string executable = (await installer.InspectAsync()).ExecutablePath!;
        File.AppendAllText(executable, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InspectAsync());
    }

    [Fact]
    public async Task ConcurrentLeasePreventsSecondInstallation()
    {
        using Fixture fixture = new();
        ModuleInstaller installer = new(fixture.Root);
        await installer.InstallAsync(true, Verified);
        string directory = Path.Combine(fixture.Root, "modules", "installed");
        using FileStream lease = new(Path.Combine(directory, "install.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(true, Verified));
        Assert.Single(Directory.GetDirectories(directory, "slot-*"));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("file.dll:stream")]
    [InlineData("C:/outside.dll")]
    [InlineData("a/./b.dll")]
    [InlineData("a /b.dll")]
    [InlineData("CON.txt")]
    public void InvalidPathsAreRejected(string path)
    {
        using Fixture fixture = new();
        Assert.Throws<InvalidDataException>(() => ModuleInstaller.Child(fixture.Root, path));
    }

    [BundledMagickFact]
    public async Task RealBundleIsInstalledAndValidatedInItsNewSlot()
    {
        using Fixture fixture = new(false);
        Directory.CreateDirectory(fixture.Payload);
        string original = Path.GetDirectoryName(OptionalMagickDecoderTests.BundledExecutable)!;
        foreach (string directory in Directory.EnumerateDirectories(original, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(fixture.Payload, Path.GetRelativePath(original, directory)));
        foreach (string file in Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(fixture.Payload, Path.GetRelativePath(original, file)));
        ModuleInstaller installer = new(fixture.Root);
        Assert.Null((await installer.InspectAsync()).ExecutablePath);
        await installer.InstallAsync(true, async (path, token) =>
        {
            var runtime = await PerceptoX.Infrastructure.Imaging.OptionalMagickDecoder.CreateAsync(path, token);
            Assert.True(runtime.SupportsExtension(".heic"));
            Assert.True(runtime.SupportsExtension(".avif"));
        });
        ModuleInspection installed = await installer.InspectAsync();
        Assert.NotNull(installed.ExecutablePath);
        Assert.StartsWith(Path.Combine(fixture.Root, "modules", "installed"), installed.ExecutablePath, StringComparison.Ordinal);
    }

    private static Task Verified(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Assert.True(File.Exists(path));
        return Task.CompletedTask;
    }
    private sealed class InlineProgress(Action<ModuleInstallProgress> action) : IProgress<ModuleInstallProgress>
    {
        public void Report(ModuleInstallProgress value) => action(value);
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly string _parent = Path.Combine(AppContext.BaseDirectory, ".module-tests");
        internal Fixture(bool createPayload = true)
        {
            Root = Path.Combine(_parent, Guid.NewGuid().ToString("N"));
            Payload = Path.Combine(Root, "codecs", "imagemagick");
            Directory.CreateDirectory(Root);
            if (!createPayload) return;
            Directory.CreateDirectory(Payload);
            List<object> files = [];
            foreach (string name in new[] { "magick.exe", "policy.xml", "License.txt", "NOTICE.txt" })
            {
                string file = Path.Combine(Payload, name);
                File.WriteAllText(file, "synthetic-test-" + name);
                files.Add(new { path = name, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) });
            }
            File.WriteAllText(Path.Combine(Payload, "bundle-manifest.json"), JsonSerializer.Serialize(new { schema = 1, architecture = "x64", files }));
        }
        internal string Root { get; }
        internal string Payload { get; }
        public void Dispose()
        {
            Assert.Equal(_parent, Path.GetDirectoryName(Root));
            Assert.True(Guid.TryParseExact(Path.GetFileName(Root), "N", out _));
            Directory.Delete(Root, recursive: true);
        }
    }
}
