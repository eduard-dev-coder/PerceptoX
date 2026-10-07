using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
using PerceptoX.Infrastructure.Modules;

namespace PerceptoX.Infrastructure.Tests;

public sealed class ModuleUpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri CatalogUri = new("https://updates.example.org/catalog.json");

    [Fact]
    public void ValidSignedCatalogIsAccepted()
    {
        using RSA key = RSA.Create(3072);
        ModuleUpdateOffer offer = Offer();
        Assert.Equal(offer, ModuleUpdateService.VerifyCatalog(Sign(offer, key), key.ExportSubjectPublicKeyInfoPem(), CatalogUri, Now));
    }

    [Fact]
    public void WrongKeyIsRejected()
    {
        using RSA key = RSA.Create(3072), wrong = RSA.Create(3072);
        Assert.Throws<CryptographicException>(() => ModuleUpdateService.VerifyCatalog(Sign(Offer(), key), wrong.ExportSubjectPublicKeyInfoPem(), CatalogUri, Now));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("http")]
    [InlineData("other-host")]
    [InlineData("architecture")]
    [InlineData("protocol")]
    [InlineData("size")]
    [InlineData("license")]
    public void SignedButIncompatibleCatalogIsRejected(string kind)
    {
        using RSA key = RSA.Create(3072);
        ModuleUpdateOffer offer = kind switch
        {
            "expired" => Offer() with { ExpiresUtc = Now.AddDays(-1) },
            "http" => Offer() with { PackageUrl = "http://updates.example.org/module.zip" },
            "other-host" => Offer() with { PackageUrl = "https://untrusted.example.org/module.zip" },
            "architecture" => Offer() with { Architecture = "win-x86" },
            "protocol" => Offer() with { Protocol = "unknown" },
            "size" => Offer() with { Bytes = 200L * 1024 * 1024 },
            _ => Offer() with { LicenseSummary = "" }
        };
        Assert.Throws<InvalidDataException>(() => ModuleUpdateService.VerifyCatalog(Sign(offer, key), key.ExportSubjectPublicKeyInfoPem(), CatalogUri, Now));
    }

    [Fact]
    public async Task FabricatedOfferAndMissingConsentMakeNoDownloadOrDirectories()
    {
        using ModuleInstallerTests.Fixture fixture = new(false);
        ModuleUpdateService service = new(fixture.Root);
        Assert.False(service.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(Offer(), false, (_, _) => Task.CompletedTask));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(Offer() with { ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1) }, true, (_, _) => Task.CompletedTask));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "modules")));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/escape.dll")]
    [InlineData("a.dll:stream")]
    public async Task ZipTraversalIsRejected(string entry)
    {
        using ModuleInstallerTests.Fixture fixture = new(false);
        string archive = Path.Combine(fixture.Root, "archive.zip"), output = Path.Combine(fixture.Root, "extracted");
        Directory.CreateDirectory(output);
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry(entry);
        await Assert.ThrowsAsync<InvalidDataException>(() => ModuleUpdateService.ExtractAsync(archive, output, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task StartupChecksAreOptInAndPersistOnlyAfterExplicitSelection()
    {
        using ModuleInstallerTests.Fixture fixture = new();
        ModuleUpdateService service = new(fixture.Root);
        Assert.False(await service.ShouldCheckAtStartupAsync());
        Directory.CreateDirectory(Path.Combine(fixture.Root, "modules"));
        File.WriteAllText(Path.Combine(fixture.Root, "modules", "update-source.json"), "{}");
        Assert.False(await service.ShouldCheckAtStartupAsync());
        await service.SetStartupChecksAsync(true);
        Assert.True(await service.ShouldCheckAtStartupAsync());
        await service.SetStartupChecksAsync(false);
        Assert.False(await service.ShouldCheckAtStartupAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedDownloadActivatesOnlyAfterConfirmationAndExactDigest(bool tampered)
    {
        using ModuleInstallerTests.Fixture fixture = new();
        using RSA key = RSA.Create(3072);
        string archive = Path.Combine(fixture.Root, "test.zip");
        ZipFile.CreateFromDirectory(fixture.Payload, archive);
        byte[] bytes = File.ReadAllBytes(archive);
        ModuleUpdateOffer offer = Offer() with { ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1), Bytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        byte[] signed = Sign(offer, key);
        string modules = Path.Combine(fixture.Root, "modules");
        Directory.CreateDirectory(modules);
        File.WriteAllText(Path.Combine(modules, "update-source.json"), JsonSerializer.Serialize(new { catalogUrl = CatalogUri.AbsoluteUri, publicKeyPem = key.ExportSubjectPublicKeyInfoPem() }));
        int requests = 0;
        if (tampered) bytes[bytes.Length / 2] ^= 1;
        ModuleUpdateService service = new(fixture.Root, () => new HttpClient(new FakeHandler(request =>
        {
            requests++;
            return request.RequestUri!.AbsolutePath.EndsWith("catalog.json", StringComparison.Ordinal) ? signed : bytes;
        })));
        ModuleUpdateOffer? selected = await service.CheckAsync("0.0.0.0");
        Assert.NotNull(selected);
        Assert.Equal(1, requests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(selected, false, (_, _) => Task.CompletedTask));
        Assert.Equal(1, requests);
        if (tampered)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyAsync(selected, true, (_, _) => Task.CompletedTask));
            Assert.Null((await new ModuleInstaller(fixture.Root).InspectAsync()).ExecutablePath);
        }
        else
        {
            await service.ApplyAsync(selected, true, (_, _) => Task.CompletedTask);
            ModuleInspection installed = await new ModuleInstaller(fixture.Root).InspectAsync();
            Assert.NotNull(installed.ExecutablePath);
            Assert.Equal(offer.Version, installed.Version);
            Assert.Null(await service.CheckAsync(installed.Version));
        }
        Assert.Empty(Directory.GetDirectories(Path.Combine(modules, "downloads")));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, byte[]> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response(request)) });
        }
    }

    private static ModuleUpdateOffer Offer() => new(1, "win-x64", "perceptox-module-v1", "imagemagick", "7.1.2.27",
        "https://updates.example.org/module.zip", new string('A', 64), 100, Now.AddDays(1), "Third-party notices supplied.");

    private static byte[] Sign(ModuleUpdateOffer offer, RSA key)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(offer);
        return JsonSerializer.SerializeToUtf8Bytes(new { payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) });
    }
}
