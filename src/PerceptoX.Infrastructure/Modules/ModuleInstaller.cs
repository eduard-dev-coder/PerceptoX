using System.Security.Cryptography;
using System.Text.Json;

namespace PerceptoX.Infrastructure.Modules;

public sealed record ModuleInspection(bool PayloadAvailable, string? ExecutablePath, string Description, string Version = "0.0.0.0");
public sealed record ModuleInstallProgress(int Completed, int Total, string FileName);

/// <summary>Consent-gated, app-local codec slots. No machine installer, registry or PATH changes.</summary>
public sealed class ModuleInstaller
{
    private const string ManifestName = "bundle-manifest.json";
    private const long MaximumBytes = 256L * 1024 * 1024;
    private readonly string _payload;
    private readonly string _installed;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ModuleInstaller(string applicationDirectory)
        : this(applicationDirectory, Path.Combine(applicationDirectory, "codecs", "imagemagick")) { }

    internal ModuleInstaller(string applicationDirectory, string payloadDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        if (!Path.IsPathFullyQualified(applicationDirectory)) throw new ArgumentException("Absolute application directory required.", nameof(applicationDirectory));
        string root = Path.GetFullPath(applicationDirectory);
        _payload = Path.GetFullPath(payloadDirectory);
        _installed = Path.Combine(root, "modules", "installed");
    }

    public async Task<ModuleInspection> InspectAsync(CancellationToken token = default)
    {
        bool payloadAvailable = File.Exists(Path.Combine(_payload, ManifestName));
        if (payloadAvailable) await ValidateAsync(_payload, token).ConfigureAwait(false);
        string pointer = Path.Combine(_installed, "active.json");
        if (!File.Exists(pointer)) return new(payloadAvailable, null, "HEIC/HEIF/AVIF: instalare necesară; imaginile native rămân disponibile.");
        ActiveSlot active = await ReadJsonAsync<ActiveSlot>(pointer, token).ConfigureAwait(false);
        if (active.Schema != 1 || !Guid.TryParseExact(active.Slot, "N", out _) || !Version.TryParse(active.Version, out _)) throw new InvalidDataException("Invalid active module slot.");
        string slot = Path.Combine(_installed, "slot-" + active.Slot);
        if (await DigestAsync(Path.Combine(slot, ManifestName), token).ConfigureAwait(false) != active.ManifestSha256)
            throw new InvalidDataException("Installed module inventory changed. Repair the module.");
        await ValidateAsync(slot, token).ConfigureAwait(false);
        return new(payloadAvailable, Path.Combine(slot, "magick.exe"), "HEIC/HEIF/AVIF: instalat și verificat.", active.Version);
    }

    public async Task InstallAsync(bool confirmed, Func<string, CancellationToken, Task> validateRuntime,
        IProgress<ModuleInstallProgress>? progress = null, string version = "7.1.2.26", CancellationToken token = default)
    {
        if (!confirmed) throw new InvalidOperationException("User confirmation is required before installing a module.");
        ArgumentNullException.ThrowIfNull(validateRuntime);
        if (!Version.TryParse(version, out _)) throw new ArgumentException("Invalid module version.", nameof(version));
        token.ThrowIfCancellationRequested();
        BundleManifest manifest = await ValidateAsync(_payload, token).ConfigureAwait(false);
        RejectReparse(_installed);
        Directory.CreateDirectory(_installed);
        RejectReparse(_installed);
        using FileStream lease = new(Path.Combine(_installed, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string id = Guid.NewGuid().ToString("N");
        string stage = Path.Combine(_installed, "stage-" + id), slot = Path.Combine(_installed, "slot-" + id);
        string temporaryPointer = Path.Combine(_installed, "active-" + id + ".tmp");
        bool committed = false;
        Directory.CreateDirectory(stage);
        try
        {
            int completed = 0;
            foreach (BundleFile entry in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                string source = Child(_payload, entry.Path), target = Child(stage, entry.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                RejectReparse(source);
                RejectReparse(target);
                await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                await using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
                progress?.Report(new(++completed, manifest.Files.Length, entry.Path));
            }
            File.Copy(Path.Combine(_payload, ManifestName), Path.Combine(stage, ManifestName));
            await ValidateAsync(stage, token).ConfigureAwait(false);
            await validateRuntime(Path.Combine(stage, "magick.exe"), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Directory.Move(stage, slot);
            ActiveSlot active = new(1, id, await DigestAsync(Path.Combine(slot, ManifestName), token).ConfigureAwait(false), version);
            await using (FileStream pointer = new(temporaryPointer, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(pointer, active, cancellationToken: token).ConfigureAwait(false);
                await pointer.FlushAsync(token).ConfigureAwait(false);
                pointer.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            RejectReparse(Path.Combine(_installed, "active.json"));
            File.Move(temporaryPointer, Path.Combine(_installed, "active.json"), overwrite: true);
            committed = true; // No cancellation after publication: the next launch selects the verified slot.
        }
        finally
        {
            if (File.Exists(temporaryPointer)) { RejectReparse(temporaryPointer); File.Delete(temporaryPointer); }
            DeleteOwned(stage, "stage-", id);
            if (!committed) DeleteOwned(slot, "slot-", id);
        }
    }

    internal static async Task<BundleManifest> ValidateAsync(string root, CancellationToken token)
    {
        BundleManifest manifest = await ReadJsonAsync<BundleManifest>(Path.Combine(root, ManifestName), token).ConfigureAwait(false);
        if (manifest.Schema != 1 || manifest.Architecture != "x64" || manifest.Files is not { Length: > 0 and <= 512 })
            throw new InvalidDataException("Unsupported module inventory.");
        HashSet<string> expected = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (BundleFile entry in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Sha256 is null || entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid module digest.");
            string file = Child(root, entry.Path);
            if (!expected.Add(file)) throw new InvalidDataException("Duplicate module path.");
            RejectReparse(file);
            total = checked(total + new FileInfo(file).Length);
            if (total > MaximumBytes) throw new InvalidDataException("Module size limit exceeded.");
            if (!string.Equals(await DigestAsync(file, token).ConfigureAwait(false), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Module hash mismatch: " + entry.Path);
        }
        foreach (string required in new[] { "magick.exe", "License.txt", "NOTICE.txt", "policy.xml" })
            if (!expected.Contains(Path.Combine(root, required))) throw new InvalidDataException("Missing module file: " + required);
        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)) CheckTree(directory);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            if (file != Path.Combine(root, ManifestName) && !expected.Contains(file)) throw new InvalidDataException("Unexpected module file.");
        return manifest;
    }

    internal static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal) ||
            relative.Split(['/', '\\']).Any(part => part is ".." or "." or "" || part.EndsWith('.') || part.EndsWith(' ')))
            throw new InvalidDataException("Invalid module path.");
        foreach (string part in relative.Split(['/', '\\']))
        {
            string name = part.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "NUL" or "PRN" or "AUX" || (name.Length == 4 && name[3] is >= '1' and <= '9' && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))))
                throw new InvalidDataException("Reserved module path.");
        }
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Module path escaped root.");
        return path;
    }

    internal static void RejectReparse(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (cursor is not null)
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Module paths cannot contain reparse points.");
            cursor = Path.GetDirectoryName(cursor);
        }
    }

    private static void CheckTree(string directory)
    {
        RejectReparse(directory);
        foreach (string child in Directory.EnumerateDirectories(directory)) CheckTree(child);
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken token)
    {
        RejectReparse(path);
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Module metadata too large.");
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Empty module metadata.");
    }

    private static async Task<string> DigestAsync(string path, CancellationToken token)
    {
        RejectReparse(path);
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }

    private void DeleteOwned(string directory, string prefix, string id)
    {
        if (!Directory.Exists(directory)) return;
        if (Path.GetDirectoryName(directory) != _installed || Path.GetFileName(directory) != prefix + id || !Guid.TryParseExact(id, "N", out _))
            throw new IOException("Module cleanup escaped owned staging/slot.");
        CheckTree(directory);
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) RejectReparse(file);
        Directory.Delete(directory, recursive: true);
    }

    internal sealed record BundleManifest(int Schema, string Architecture, BundleFile[] Files);
    internal sealed record BundleFile(string Path, string Sha256);
    private sealed record ActiveSlot(int Schema, string Slot, string ManifestSha256, string Version);
}
