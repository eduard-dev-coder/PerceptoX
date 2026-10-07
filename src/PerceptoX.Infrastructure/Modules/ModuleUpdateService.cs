using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PerceptoX.Infrastructure.Modules;

public sealed record ModuleUpdateOffer(int Schema, string Architecture, string Protocol, string Module,
    string Version, string PackageUrl, string Sha256, long Bytes, DateTimeOffset ExpiresUtc, string LicenseSummary);

/// <summary>Updates require an explicitly configured HTTPS feed and trusted RSA public key.</summary>
public sealed class ModuleUpdateService
{
    private const int MaximumCatalogBytes = 64 * 1024;
    private const long MaximumPackageBytes = 128L * 1024 * 1024;
    private readonly string _applicationDirectory;
    private readonly string _configuration;
    private readonly HashSet<string> _issuedOffers = new(StringComparer.Ordinal);
    private readonly Func<HttpClient> _client;

    public ModuleUpdateService(string applicationDirectory)
        : this(applicationDirectory, Client) { }

    internal ModuleUpdateService(string applicationDirectory, Func<HttpClient> client)
    {
        _applicationDirectory = Path.GetFullPath(applicationDirectory);
        _configuration = Path.Combine(_applicationDirectory, "modules", "update-source.json");
        _client = client;
    }

    public bool IsConfigured => File.Exists(_configuration);

    public async Task<bool> ShouldCheckAtStartupAsync(CancellationToken token = default)
    {
        string preference = Path.Combine(_applicationDirectory, "modules", "check-on-startup.json");
        if (!IsConfigured || !File.Exists(preference)) return false;
        ModuleInstaller.RejectReparse(preference);
        await using FileStream input = File.OpenRead(preference);
        if (input.Length > 128) throw new InvalidDataException("Invalid startup update preference.");
        return await JsonSerializer.DeserializeAsync<bool>(input, cancellationToken: token).ConfigureAwait(false);
    }

    public async Task SetStartupChecksAsync(bool enabled, CancellationToken token = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Configure a trusted source before enabling online checks.");
        string parent = Path.Combine(_applicationDirectory, "modules");
        string target = Path.Combine(parent, "check-on-startup.json");
        ModuleInstaller.RejectReparse(target);
        string temporary = Path.Combine(parent, "preference-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, enabled ? "true" : "false", token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) { ModuleInstaller.RejectReparse(temporary); File.Delete(temporary); } }
    }

    public async Task<ModuleUpdateOffer?> CheckAsync(string installedVersion, CancellationToken token = default)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        token = deadline.Token;
        UpdateSource source = await ReadSourceAsync(token).ConfigureAwait(false);
        Uri catalogUrl = Https(source.CatalogUrl);
        using HttpClient http = _client();
        using HttpResponseMessage response = await http.GetAsync(catalogUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using MemoryStream bytes = new();
        await CopyBoundedAsync(input, bytes, MaximumCatalogBytes, token).ConfigureAwait(false);
        ModuleUpdateOffer offer = VerifyCatalog(bytes.ToArray(), source.PublicKeyPem, catalogUrl, DateTimeOffset.UtcNow);
        if (!Version.TryParse(installedVersion, out Version? current)) throw new InvalidDataException("Installed module version is invalid.");
        if (Version.Parse(offer.Version) <= current) return null;
        _issuedOffers.Add(OfferId(offer));
        return offer;
    }

    public async Task ApplyAsync(ModuleUpdateOffer offer, bool confirmed, Func<string, CancellationToken, Task> validateRuntime,
        IProgress<ModuleInstallProgress>? progress = null, CancellationToken token = default)
    {
        if (!confirmed) throw new InvalidOperationException("Confirm the update and its license notice before downloading.");
        ArgumentNullException.ThrowIfNull(offer);
        if (!_issuedOffers.Remove(OfferId(offer)) || offer.ExpiresUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Check the signed catalog again before updating.");
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        token = deadline.Token;
        ModuleInspection current = await new ModuleInstaller(_applicationDirectory).InspectAsync(token).ConfigureAwait(false);
        if (Version.Parse(offer.Version) <= Version.Parse(current.Version)) throw new InvalidOperationException("Update would not advance the installed version.");
        string parent = Path.Combine(_applicationDirectory, "modules", "downloads");
        ModuleInstaller.RejectReparse(parent);
        Directory.CreateDirectory(parent);
        string owned = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        try
        {
            string archive = Path.Combine(owned, "package.zip"), extracted = Path.Combine(owned, "payload");
            Directory.CreateDirectory(extracted);
            using HttpClient http = _client();
            using HttpResponseMessage response = await http.GetAsync(Https(offer.PackageUrl), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (FileStream output = new(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyBoundedAsync(input, output, offer.Bytes, token).ConfigureAwait(false);
            if (new FileInfo(archive).Length != offer.Bytes) throw new InvalidDataException("Update length mismatch.");
            await using (FileStream input = File.OpenRead(archive))
                if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)), offer.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update SHA-256 mismatch.");
            await ExtractAsync(archive, extracted, token).ConfigureAwait(false);
            ModuleInstaller installer = new(_applicationDirectory, extracted);
            await installer.InstallAsync(true, validateRuntime, progress, offer.Version, token).ConfigureAwait(false);
        }
        finally
        {
            DeleteOwnedDownload(parent, owned);
        }
    }

    internal static ModuleUpdateOffer VerifyCatalog(byte[] json, string publicKey, Uri catalogUrl, DateTimeOffset now)
    {
        if (json.Length > MaximumCatalogBytes) throw new InvalidDataException("Catalog too large.");
        SignedCatalog catalog = JsonSerializer.Deserialize<SignedCatalog>(json, JsonOptions) ?? throw new InvalidDataException("Empty catalog.");
        byte[] payload = Convert.FromBase64String(catalog.Payload), signature = Convert.FromBase64String(catalog.Signature);
        if (publicKey.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new InvalidDataException("A public signing key is required.");
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (rsa.KeySize < 3072 || !rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("Catalog signature is invalid or key is too small.");
        ModuleUpdateOffer offer = JsonSerializer.Deserialize<ModuleUpdateOffer>(payload, JsonOptions) ?? throw new InvalidDataException("Empty offer.");
        Uri packageUrl = Https(offer.PackageUrl);
        if (!Https(catalogUrl.AbsoluteUri).Host.Equals(packageUrl.Host, StringComparison.OrdinalIgnoreCase) || catalogUrl.Port != packageUrl.Port ||
            offer.Schema != 1 || offer.Architecture != "win-x64" || offer.Protocol != "perceptox-module-v1" || offer.Module != "imagemagick" ||
            !Version.TryParse(offer.Version, out _) || offer.Bytes is <= 0 or > MaximumPackageBytes ||
            offer.Sha256 is null || offer.Sha256.Length != 64 || !offer.Sha256.All(Uri.IsHexDigit) ||
            offer.ExpiresUtc <= now || offer.ExpiresUtc > now.AddDays(31) || string.IsNullOrWhiteSpace(offer.LicenseSummary) || offer.LicenseSummary.Length > 4096)
            throw new InvalidDataException("Catalog is expired, incompatible or outside the trusted source.");
        return offer;
    }

    internal static async Task ExtractAsync(string archive, string destination, CancellationToken token)
    {
        using ZipArchive zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count is 0 or > 1024) throw new InvalidDataException("Invalid update entry count.");
        HashSet<string> entries = new(StringComparer.OrdinalIgnoreCase);
        long remaining = 256L * 1024 * 1024;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string name = entry.FullName.TrimEnd('/');
            string target = ModuleInstaller.Child(destination, name);
            if (!entries.Add(target) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("Duplicate or symbolic update entry.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            if (entry.Length > remaining) throw new InvalidDataException("Expanded update too large.");
            ModuleInstaller.RejectReparse(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using Stream input = entry.Open();
            await using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long copied = await CopyBoundedAsync(input, output, remaining, token).ConfigureAwait(false);
            remaining -= copied;
        }
    }

    private async Task<UpdateSource> ReadSourceAsync(CancellationToken token)
    {
        ModuleInstaller.RejectReparse(_configuration);
        await using FileStream stream = File.OpenRead(_configuration);
        if (stream.Length > 16384) throw new InvalidDataException("Update configuration too large.");
        return await JsonSerializer.DeserializeAsync<UpdateSource>(stream, JsonOptions, token).ConfigureAwait(false) ?? throw new InvalidDataException("Update source missing.");
    }

    private static Uri Https(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.IsLoopback)
            throw new InvalidDataException("A non-loopback HTTPS update source is required.");
        return uri;
    }

    private static HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private static string OfferId(ModuleUpdateOffer offer) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(offer)));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record UpdateSource(string CatalogUrl, string PublicKeyPem);
    private sealed record SignedCatalog(string Payload, string Signature);

    private static async Task<long> CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        byte[] buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("Update transfer exceeded the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        return total;
    }

    private static void RejectTree(string directory)
    {
        ModuleInstaller.RejectReparse(directory);
        foreach (string child in Directory.EnumerateDirectories(directory)) RejectTree(child);
        foreach (string file in Directory.EnumerateFiles(directory)) ModuleInstaller.RejectReparse(file);
    }

    private static void DeleteOwnedDownload(string parent, string owned)
    {
        if (Path.GetDirectoryName(owned) != parent || !Guid.TryParseExact(Path.GetFileName(owned), "N", out _)) throw new IOException("Download cleanup escaped owned directory.");
        RejectTree(owned);
        Directory.Delete(owned, recursive: true);
    }
}
