using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using PerceptoX.Application.Matching;
using PerceptoX.Infrastructure.Maintenance;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;
using PerceptoX.Application.Imaging;

namespace PerceptoX.Infrastructure.Imaging;

/// <summary>Bounded RGB thumbnail verification plus streamed SHA-256, never fingerprint identity alone.</summary>
public sealed class GroupingImageVerifier : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _detail;
    private readonly string _cache;
    private readonly CancellationToken _token;
    private readonly ImageSharpImageProcessor? _processor;
    private long _anchorId;
    private Source? _anchor;
    private byte[]? _anchorPixels, _anchorHash;
    public int Failed { get; private set; }
    public int Rejected { get; private set; }

    private sealed record Source(long Id, string Path, long Size, long Ticks, string SourceVersion, string? Thumbnail);

    public GroupingImageVerifier(string databasePath, string cacheRoot, CancellationToken token,
        ImageSharpImageProcessor? processor = null)
    {
        _cache = Path.GetFullPath(cacheRoot); _token = token; _processor = processor;
        _connection = new(new SqliteConnectionStringBuilder { DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        _connection.Open();
        using (var schema = _connection.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA synchronous=NORMAL;
                CREATE TABLE IF NOT EXISTS ImageContentHashes(ImageId INTEGER PRIMARY KEY,FileSize INTEGER NOT NULL,
                    LastWriteTicks INTEGER NOT NULL,Hash BLOB NOT NULL CHECK(length(Hash)=32));
                """;
            schema.ExecuteNonQuery();
        }
        _detail = _connection.CreateCommand();
        _detail.CommandText = """
            SELECT i.FilePath,i.FileSize,i.LastWriteUtcTicks,t.RelativePath,i.SourceVersion
            FROM Images i LEFT JOIN Thumbnails t ON t.ImageId=i.Id WHERE i.Id=$id AND i.IsActive=1;
            """;
        _detail.Parameters.Add("$id", SqliteType.Integer);
    }

    public GroupingVerification Verify(SearchIndexEntry anchor, SearchIndexEntry candidate)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            if (_anchorId != anchor.ImageId)
            {
                _anchorId = 0; _anchor = null;
                _anchorPixels = null; _anchorHash = null;
                _anchor = ReadSource(anchor.ImageId);
                _anchorId = anchor.ImageId;
            }
            Source reference = _anchor ?? throw new IOException("Representative unavailable.");
            Source other = ReadSource(candidate.ImageId);
            CheckVersion(reference); CheckVersion(other);
            if (reference.Size == other.Size)
            {
                _anchorHash ??= ContentHash(reference);
                if (_anchorHash.AsSpan().SequenceEqual(ContentHash(other))) return new(true, true, 0);
            }
            double ratio = ((double)anchor.Width / anchor.Height) / ((double)candidate.Width / candidate.Height);
            if (Math.Abs(1 - ratio) > 0.02) { Rejected++; return default; }
            _anchorPixels ??= LoadPixels(reference);
            byte[] pixels = LoadPixels(other);
            // Low-information images require binary proof; solid colors cannot become a perceptual family.
            if (SpatialVariance(_anchorPixels) < 4 || SpatialVariance(pixels) < 4)
            { Rejected++; return default; }
            double squared = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                double difference = pixels[i] - _anchorPixels[i]; squared += difference * difference;
            }
            double rms = Math.Sqrt(squared / pixels.Length) / 255;
            CheckVersion(reference); CheckVersion(other);
            if (rms > 0.06) { Rejected++; return new(false, false, rms); }
            return new(true, false, rms);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException)
        { Failed++; return default; }
    }

    private Source ReadSource(long id)
    {
        _detail.Parameters[0].Value = id;
        using var reader = _detail.ExecuteReader();
        if (!reader.Read()) throw new IOException("Indexed image unavailable.");
        string? thumbnail = reader.IsDBNull(3) ? null : Path.GetFullPath(Path.Combine(_cache, reader.GetString(3)));
        if (thumbnail is not null && !ManagedPaths.Contains(_cache, thumbnail)) throw new IOException("Invalid thumbnail path.");
        return new(id, reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(4), thumbnail);
    }

    private static void CheckVersion(Source source)
    {
        ManagedPaths.RejectLinks(source.Path);
        FileInfo info = new(source.Path);
        if (!info.Exists || info.Length != source.Size || info.LastWriteTimeUtc.Ticks != source.Ticks)
            throw new IOException("Image changed since indexing; reindex before analysis.");
    }

    private byte[] ContentHash(Source source)
    {
        CheckVersion(source);
        using var cached = _connection.CreateCommand();
        cached.CommandText = "SELECT Hash FROM ImageContentHashes WHERE ImageId=$id AND FileSize=$size AND LastWriteTicks=$ticks;";
        cached.Parameters.AddWithValue("$id", source.Id); cached.Parameters.AddWithValue("$size", source.Size);
        cached.Parameters.AddWithValue("$ticks", source.Ticks);
        if (cached.ExecuteScalar() is byte[] existing) return existing;
        using var stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        int count;
        while ((count = stream.Read(buffer)) > 0) { _token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        CheckVersion(source);
        byte[] result = hash.GetHashAndReset();
        cached.CommandText = "INSERT INTO ImageContentHashes VALUES($id,$size,$ticks,$hash) ON CONFLICT(ImageId) DO UPDATE SET FileSize=excluded.FileSize,LastWriteTicks=excluded.LastWriteTicks,Hash=excluded.Hash;";
        cached.Parameters.AddWithValue("$hash", result); cached.ExecuteNonQuery();
        return result;
    }

    private byte[] LoadPixels(Source source)
    {
        _token.ThrowIfCancellationRequested();
        string? path = source.Thumbnail;
        if (path is null || !File.Exists(path))
        {
            if (_processor is null) throw new IOException("Thumbnail unavailable; regenerate cache before analysis.");
            CheckVersion(source);
            var thumbnail = _processor.RegenerateThumbnailAsync(new ImageProcessingRequest(source.Path, source.Id,
                source.SourceVersion, _cache), _token).GetAwaiter().GetResult();
            path = Path.GetFullPath(Path.Combine(_cache, thumbnail.RelativePath));
            CheckVersion(source);
        }
        ManagedPaths.RejectLinks(path);
        var options = new DecoderOptions { MaxFrames = 1, SkipMetadata = true };
        var info = Image.Identify(options, path);
        if (info.Width > 256 || info.Height > 256) throw new IOException("Unexpected cache image dimensions.");
        using var image = Image.Load<Rgb24>(options, path);
        image.Mutate(context => context.Resize(32, 32));
        byte[] pixels = new byte[32 * 32 * 3];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static double SpatialVariance(byte[] pixels)
    {
        double result = 0;
        for (int channel = 0; channel < 3; channel++)
        {
            double sum = 0, squares = 0;
            for (int i = channel; i < pixels.Length; i += 3) { sum += pixels[i]; squares += pixels[i] * pixels[i]; }
            double mean = sum / 1024; result += squares / 1024 - mean * mean;
        }
        return result / 3;
    }

    public void Dispose() { _detail.Dispose(); _connection.Dispose(); }
}
