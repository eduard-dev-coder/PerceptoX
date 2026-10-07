using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace PerceptoX.Infrastructure.Imaging;

/// <summary>An adapter to an explicitly selected ImageMagick runtime, including an app-local bundle.</summary>
public sealed class OptionalMagickDecoder
{
    private const long MaximumPixels = 100_000_000;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(60);
    private readonly string _executablePath;
    private readonly FileIdentity _executableIdentity;
    private readonly bool _heic;
    private readonly bool _heif;
    private readonly bool _avif;

    private OptionalMagickDecoder(string executablePath, FileIdentity identity, string version,
        string runtimeProfileId, bool heic, bool heif, bool avif)
    {
        _executablePath = executablePath;
        _executableIdentity = identity;
        RuntimeVersion = version;
        RuntimeProfileId = runtimeProfileId;
        _heic = heic;
        _heif = heif;
        _avif = avif;
    }

    public string RuntimeVersion { get; }
    public string RuntimeProfileId { get; }

    public bool SupportsExtension(string extension) => extension?.ToUpperInvariant() switch
    {
        ".HEIC" => _heic,
        ".HEIF" => _heif,
        ".AVIF" => _avif,
        _ => false
    };

    public static async Task<OptionalMagickDecoder> CreateAsync(string executablePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("Select an absolute ImageMagick executable path.", nameof(executablePath));
        string executable = Path.GetFullPath(executablePath);
        using FileStream executableLock = OpenStableFile(executable);
        FileIdentity identity = FileIdentity.Read(executable);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(OperationTimeout);
        string directory = CreateOwnedDirectory();
        try
        {
            string versionOutput = await RunAsync(executable, directory, ["-version"], deadline.Token, cancellationToken).ConfigureAwait(false);
            string version = versionOutput.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (!version.StartsWith("Version: ImageMagick 7.", StringComparison.Ordinal))
                throw new NotSupportedException("The optional decoder requires ImageMagick 7.");
            string formats = await RunAsync(executable, directory, ["-list", "format"], deadline.Token, cancellationToken).ConfigureAwait(false);
            string[] codecLines = formats.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(line => IsReadableFormat(line, "HEIC") || IsReadableFormat(line, "HEIF") || IsReadableFormat(line, "AVIF")).ToArray();
            bool heic = codecLines.Any(line => IsReadableFormat(line, "HEIC"));
            bool heif = codecLines.Any(line => IsReadableFormat(line, "HEIF"));
            bool avif = codecLines.Any(line => IsReadableFormat(line, "AVIF"));
            if (!heic && !heif && !avif)
                throw new NotSupportedException("The selected runtime has no readable HEIC, HEIF or AVIF codec.");
            EnsureIdentity(executable, identity, executableLock);
            byte[] binaryDigest = await SHA256.HashDataAsync(executableLock, deadline.Token).ConfigureAwait(false);
            string profile = "magick-canonical-png-v1|" + executable + "|" + versionOutput.Trim() + "|" +
                string.Join('|', codecLines) + "|" + Convert.ToHexString(binaryDigest) +
                "|frame=0|auto-orient|sRGB|depth=8|strip|memory=256MiB|map=512MiB|disk=0|threads=2";
            string profileId = "optional-magick-sha256-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
            return new OptionalMagickDecoder(executable, identity, version, profileId, heic, heif, avif);
        }
        finally { DeleteOwnedDirectory(directory); }
    }

    public async Task<OptionalMagickDecodedImage> DecodeToTemporaryPngAsync(string originalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        cancellationToken.ThrowIfCancellationRequested();
        string original = Path.GetFullPath(originalPath);
        if (!SupportsExtension(Path.GetExtension(original)))
            throw new NotSupportedException("The selected optional runtime cannot read this image extension.");
        using FileStream originalLock = OpenStableFile(original);
        FileIdentity originalIdentity = FileIdentity.Read(original);
        using FileStream executableLock = OpenStableFile(_executablePath);
        EnsureIdentity(_executablePath, _executableIdentity, executableLock);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(OperationTimeout);
        string directory = CreateOwnedDirectory();
        try
        {
            // Explicit coder and frame zero avoid ambiguous extension sniffing and multi-image output.
            string coder = Path.GetExtension(original).Equals(".avif", StringComparison.OrdinalIgnoreCase) ? "AVIF:" : "HEIC:";
            string input = coder + original + "[0]";
            List<string> identify = ["identify", .. Limits(), "-ping", "-format", "%w %h", input];
            string header = await RunAsync(_executablePath, directory, identify, deadline.Token, cancellationToken).ConfigureAwait(false);
            (int headerWidth, int headerHeight) = ReadDimensions(header);
            ValidatePixels(headerWidth, headerHeight);
            EnsureIdentity(original, originalIdentity, originalLock);
            string png = Path.Combine(directory, "canonical.png");
            List<string> decode = [.. Limits(), input, "-auto-orient", "-colorspace", "sRGB", "-depth", "8", "-strip",
                "-write", "PNG:" + png, "-format", "%w %h", "info:"];
            string output = await RunAsync(_executablePath, directory, decode, deadline.Token, cancellationToken).ConfigureAwait(false);
            (int width, int height) = ReadDimensions(output);
            ValidatePixels(width, height);
            if (!((width == headerWidth && height == headerHeight) || (width == headerHeight && height == headerWidth)))
                throw new InvalidDataException("The decoder changed the source dimensions beyond orientation.");
            EnsureIdentity(original, originalIdentity, originalLock);
            EnsureIdentity(_executablePath, _executableIdentity, executableLock);
            using FileStream pngLock = OpenStableFile(png);
            Span<byte> signature = stackalloc byte[8];
            pngLock.ReadExactly(signature);
            if (!signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException("The decoder did not produce a PNG.");
            pngLock.Position = 0;
            ImageInfo info = Image.Identify(new DecoderOptions { MaxFrames = 1, SkipMetadata = true }, pngLock);
            if (info.Width != width || info.Height != height)
                throw new InvalidDataException("The canonical PNG dimensions do not match the decoder result.");
            deadline.Token.ThrowIfCancellationRequested();
            return new OptionalMagickDecodedImage(directory, png, width, height);
        }
        catch
        {
            DeleteOwnedDirectory(directory);
            throw;
        }
    }

    private static string[] Limits() =>
        ["-limit", "memory", "256MiB", "-limit", "map", "512MiB", "-limit", "disk", "0", "-limit", "thread", "2"];

    private static bool IsReadableFormat(string line, string format)
    {
        string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 3 && fields[0].TrimEnd('*') == format && fields[2].StartsWith('r');
    }

    private static (int Width, int Height) ReadDimensions(string output)
    {
        string[] fields = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 2 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height) || width <= 0 || height <= 0)
            throw new InvalidDataException("The decoder returned invalid dimensions.");
        return (width, height);
    }

    private static void ValidatePixels(int width, int height)
    {
        if (checked((long)width * height) > MaximumPixels)
            throw new InvalidDataException("Image exceeds the configured 100-megapixel safety limit.");
    }

    private static async Task<string> RunAsync(string executable, string directory, IEnumerable<string> arguments,
        CancellationToken deadline, CancellationToken caller)
    {
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = directory
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        string runtimeDirectory = Path.GetDirectoryName(executable)!;
        // Resolve app-local configuration and modules, not the developer's registry/configuration.
        start.Environment["MAGICK_HOME"] = runtimeDirectory;
        start.Environment["MAGICK_CONFIGURE_PATH"] = runtimeDirectory;
        start.Environment["MAGICK_CODER_MODULE_PATH"] = Path.Combine(runtimeDirectory, "modules", "coders");
        start.Environment["MAGICK_FILTER_MODULE_PATH"] = Path.Combine(runtimeDirectory, "modules", "filters");
        start.Environment["PATH"] = runtimeDirectory + Path.PathSeparator + Environment.GetFolderPath(Environment.SpecialFolder.System);
        start.Environment["MAGICK_TEMPORARY_PATH"] = directory;
        start.Environment["MAGICK_THREAD_LIMIT"] = "2";
        start.Environment["MAGICK_MEMORY_LIMIT"] = "256MiB";
        start.Environment["MAGICK_MAP_LIMIT"] = "512MiB";
        start.Environment["MAGICK_DISK_LIMIT"] = "0";
        using Process process = new() { StartInfo = start };
        deadline.ThrowIfCancellationRequested();
        if (!process.Start()) throw new IOException("The optional decoder process did not start.");
        Task<string> stdout = DrainAsync(process.StandardOutput, deadline);
        Task<string> stderr = DrainAsync(process.StandardError, deadline);
        Task completion = Task.WhenAll(process.WaitForExitAsync(deadline), stdout, stderr);
        try { await completion.WaitAsync(deadline).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(5));
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(shutdown.Token), stdout, stderr)
                    .WaitAsync(shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            caller.ThrowIfCancellationRequested();
            throw new TimeoutException("The optional decoder exceeded its 60-second operation deadline.");
        }
        string output = await stdout.ConfigureAwait(false);
        string error = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException("The optional decoder failed: " + error.Trim());
        return output;
    }

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        const int outputLimit = 64 * 1024;
        StringBuilder captured = new();
        char[] buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            int retained = Math.Min(count, outputLimit - captured.Length);
            if (retained > 0) captured.Append(buffer, 0, retained);
        }
        return captured.ToString();
    }

    private static FileStream OpenStableFile(string path)
    {
        RejectReparsePath(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void RejectReparsePath(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The optional decoder does not accept reparse-point paths.");
            current = Path.GetDirectoryName(current);
        }
    }

    private static void EnsureIdentity(string path, FileIdentity identity, FileStream stream)
    {
        RejectReparsePath(path);
        if (FileIdentity.Read(path) != identity || stream.Length != identity.Length)
            throw new IOException("The source or selected decoder changed during the operation.");
    }

    private static string CreateOwnedDirectory()
    {
        string parent = Path.GetFullPath(Path.GetTempPath());
        RejectReparsePath(parent);
        string directory = Path.Combine(parent, "PerceptoX-OptionalCodec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RejectReparsePath(directory);
        return directory;
    }

    internal static void DeleteOwnedDirectory(string directory)
    {
        string parent = Path.GetFullPath(Path.GetTempPath());
        string target = Path.GetFullPath(directory);
        if (Path.GetDirectoryName(target) != Path.TrimEndingDirectorySeparator(parent) ||
            !Path.GetFileName(target).StartsWith("PerceptoX-OptionalCodec-", StringComparison.Ordinal))
            throw new IOException("The optional decoder cleanup escaped its owned temporary directory.");
        if (!Directory.Exists(target)) return;
        RejectReparsePath(target);
        foreach (string file in Directory.EnumerateFiles(target)) RejectReparsePath(file);
        if (Directory.EnumerateDirectories(target).Any())
            throw new IOException("Unexpected subdirectories were found in decoder temporary output.");
        Directory.Delete(target, recursive: true);
    }

    private readonly record struct FileIdentity(long Length, long CreatedTicks, long ModifiedTicks)
    {
        public static FileIdentity Read(string path)
        {
            FileInfo info = new(path);
            info.Refresh();
            return new FileIdentity(info.Length, info.CreationTimeUtc.Ticks, info.LastWriteTimeUtc.Ticks);
        }
    }
}

public sealed class OptionalMagickDecodedImage : IDisposable
{
    private string? _directory;

    internal OptionalMagickDecodedImage(string directory, string pngPath, int width, int height)
    {
        _directory = directory;
        PngPath = pngPath;
        OriginalWidth = width;
        OriginalHeight = height;
    }

    public string PngPath { get; }
    public int OriginalWidth { get; }
    public int OriginalHeight { get; }

    public void Dispose()
    {
        string? directory = Interlocked.Exchange(ref _directory, null);
        if (directory is not null) OptionalMagickDecoder.DeleteOwnedDirectory(directory);
    }
}
