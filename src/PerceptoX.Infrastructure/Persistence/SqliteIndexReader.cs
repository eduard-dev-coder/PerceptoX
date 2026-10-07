using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using PerceptoX.Application.Matching;
using PerceptoX.Core.Fingerprints;
using PerceptoX.Core.Images;
using PerceptoX.Infrastructure.Indexing;

namespace PerceptoX.Infrastructure.Persistence;

public sealed class SqliteIndexReader
{
    private readonly string _databasePath;

    public SqliteIndexReader(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
    }

    public long CountActive(string libraryRoot)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM Images i
            JOIN LibraryRoots r ON r.Id=i.RootId
            WHERE r.RootKey=$root AND i.IsActive=1;
            """;
        command.Parameters.AddWithValue("$root", WindowsPathKey.Create(libraryRoot));
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public ImageRecord? FindByPath(string libraryRoot, string imagePath)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.Id,i.RootId,p.PathKey,i.FilePath,i.Width,i.Height,i.FileSize,i.LastWriteUtcTicks,i.IsActive
            FROM Images i JOIN ImageIdentities p ON p.Id=i.Id
            JOIN LibraryRoots r ON r.Id=i.RootId
            WHERE r.RootKey=$root AND p.PathKey=$path;
            """;
        command.Parameters.AddWithValue("$root", WindowsPathKey.Create(libraryRoot));
        command.Parameters.AddWithValue("$path", WindowsPathKey.Create(imagePath));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ImageRecord(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
            reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(6),
            new DateTimeOffset(reader.GetInt64(7), TimeSpan.Zero), reader.GetInt64(8) == 1);
    }

    public IReadOnlyDictionary<string, long> ResolveActiveImageIds(string libraryRoot,
        IEnumerable<string> imagePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentNullException.ThrowIfNull(imagePaths);
        Dictionary<string, long> result = new(StringComparer.OrdinalIgnoreCase);
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT i.Id FROM Images i
            JOIN ImageIdentities p ON p.Id=i.Id
            JOIN LibraryRoots r ON r.Id=i.RootId
            WHERE r.RootKey=$root AND p.PathKey=$path AND i.IsActive=1;
            """;
        command.Parameters.AddWithValue("$root", WindowsPathKey.Create(libraryRoot));
        SqliteParameter pathParameter = command.Parameters.Add("$path", SqliteType.Text);
        foreach (string imagePath in imagePaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
            string fullPath = Path.GetFullPath(imagePath);
            pathParameter.Value = WindowsPathKey.Create(fullPath);
            object? value = command.ExecuteScalar();
            if (value is not null)
            {
                result[fullPath] = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        transaction.Commit();
        return result;
    }

    public int CountFingerprints(long imageId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM ImageFingerprints WHERE ImageId=$image;";
        command.Parameters.AddWithValue("$image", imageId);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public ThumbnailRecord? FindThumbnail(long imageId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT SourceVersion,ProfileId,RelativePath,FormatId,Width,Height FROM Thumbnails WHERE ImageId=$image;";
        command.Parameters.AddWithValue("$image", imageId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new ThumbnailRecord(imageId, reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5))
            : null;
    }

    public IReadOnlyDictionary<long, SearchImageDetail> LoadDetails(IEnumerable<long> imageIds)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        long[] ids = imageIds.Distinct().ToArray();
        Dictionary<long, SearchImageDetail> details = new(ids.Length);
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT i.Id,i.RootId,p.PathKey,i.FilePath,i.Width,i.Height,i.FileSize,i.LastWriteUtcTicks,i.IsActive,
                   t.SourceVersion,t.ProfileId,t.RelativePath,t.FormatId,t.Width,t.Height
            FROM Images i JOIN ImageIdentities p ON p.Id=i.Id
            LEFT JOIN Thumbnails t ON t.ImageId=i.Id
            WHERE i.Id=$image;
            """;
        SqliteParameter imageParameter = command.Parameters.Add("$image", SqliteType.Integer);
        foreach (long imageId in ids)
        {
            imageParameter.Value = imageId;
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                continue;
            }

            ImageRecord image = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(6),
                new DateTimeOffset(reader.GetInt64(7), TimeSpan.Zero), reader.GetInt64(8) == 1);
            ThumbnailRecord? thumbnail = reader.IsDBNull(9) ? null : new ThumbnailRecord(imageId,
                reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12),
                reader.GetInt32(13), reader.GetInt32(14));
            details.Add(imageId, new SearchImageDetail(image, thumbnail));
        }

        transaction.Commit();
        return details;
    }

    public SearchIndexSnapshot LoadSnapshot(string libraryRoot, string processingProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processingProfileId);
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand rootCommand = connection.CreateCommand();
        rootCommand.Transaction = transaction;
        rootCommand.CommandText = "SELECT Id,LastCompletedScanId FROM LibraryRoots WHERE RootKey=$root;";
        rootCommand.Parameters.AddWithValue("$root", WindowsPathKey.Create(libraryRoot));
        long rootId;
        long scanId;
        using (SqliteDataReader rootReader = rootCommand.ExecuteReader())
        {
            if (!rootReader.Read())
            {
                return new SearchIndexSnapshot(0, processingProfileId, []);
            }

            rootId = rootReader.GetInt64(0);
            scanId = rootReader.IsDBNull(1) ? 0 : rootReader.GetInt64(1);
        }

        List<SearchIndexEntry> entries = [];
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT i.Id,p.HashBytes,d.HashBytes,i.Width,i.Height,i.FileSize FROM Images i
            JOIN ImageFingerprints p ON p.ImageId=i.Id AND p.AlgorithmId='phash' AND p.BitLength=64
            JOIN ImageFingerprints d ON d.ImageId=i.Id AND d.AlgorithmId='dhash' AND d.BitLength=64
            WHERE i.RootId=$root AND i.IsActive=1 AND i.ProcessingProfileId=$profile ORDER BY i.Id;
            """;
        command.Parameters.AddWithValue("$root", rootId);
        command.Parameters.AddWithValue("$profile", processingProfileId);
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            byte[] hashBuffer = new byte[sizeof(ulong)];
            while (reader.Read())
            {
                ulong perceptual = ReadHash64(reader, 1, hashBuffer);
                ulong difference = ReadHash64(reader, 2, hashBuffer);
                entries.Add(new SearchIndexEntry(reader.GetInt64(0), perceptual, difference,
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetInt64(5)));
            }
        }

        transaction.Commit();
        return new SearchIndexSnapshot(scanId, processingProfileId, CollectionsMarshal.AsSpan(entries));
    }

    /// <summary>One read transaction for all roots and fingerprints. Null scope means all active roots.</summary>
    public GroupingIndexSnapshot LoadGroupingSnapshot(IReadOnlyList<string>? roots, string processingProfileId,
        int maximumImages = 1_000_000, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processingProfileId);
        HashSet<string>? keys = roots?.Select(WindowsPathKey.Create).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        List<GroupingRootGeneration> generations = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id,LastCompletedScanId,RootPath,RootKey FROM LibraryRoots WHERE LastCompletedScanId IS NOT NULL ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (keys is null || keys.Contains(reader.GetString(3)))
                    generations.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)));
            }
        }
        List<SearchIndexEntry> entries = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT i.Id,p.HashBytes,d.HashBytes,i.Width,i.Height,i.FileSize FROM Images i
                JOIN ImageFingerprints p ON p.ImageId=i.Id AND p.AlgorithmId='phash' AND p.BitLength=64
                JOIN ImageFingerprints d ON d.ImageId=i.Id AND d.AlgorithmId='dhash' AND d.BitLength=64
                WHERE i.RootId=$root AND i.IsActive=1 AND i.ProcessingProfileId=$profile ORDER BY i.Id;
                """;
            var root = command.Parameters.Add("$root", SqliteType.Integer);
            command.Parameters.AddWithValue("$profile", processingProfileId);
            byte[] buffer = new byte[8];
            foreach (var generation in generations)
            {
                root.Value = generation.RootId;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (entries.Count >= maximumImages) throw new InvalidOperationException("Grouping image budget exceeded.");
                    entries.Add(new(reader.GetInt64(0), ReadHash64(reader, 1, buffer), ReadHash64(reader, 2, buffer),
                        reader.GetInt32(3), reader.GetInt32(4), reader.GetInt64(5)));
                }
            }
        }
        int active = 0;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction; count.CommandText = "SELECT COUNT(*) FROM Images WHERE RootId=$root AND IsActive=1;";
            var root = count.Parameters.Add("$root", SqliteType.Integer);
            foreach (var generation in generations)
            {
                token.ThrowIfCancellationRequested(); root.Value = generation.RootId;
                active = checked(active + Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        transaction.Commit();
        // ScanId=0 is deliberately not a global generation; Roots carries the complete vector.
        return new(new SearchIndexSnapshot(0, processingProfileId, CollectionsMarshal.AsSpan(entries)), generations)
            { ExcludedImages = active - entries.Count };
    }

    public MultiRegionSearchIndexSnapshot LoadMultiRegionSnapshot(
        string libraryRoot,
        string processingProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processingProfileId);
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand rootCommand = connection.CreateCommand();
        rootCommand.Transaction = transaction;
        rootCommand.CommandText = "SELECT Id,LastCompletedScanId FROM LibraryRoots WHERE RootKey=$root;";
        rootCommand.Parameters.AddWithValue("$root", WindowsPathKey.Create(libraryRoot));
        long rootId;
        long scanId;
        using (SqliteDataReader rootReader = rootCommand.ExecuteReader())
        {
            if (!rootReader.Read())
            {
                return new MultiRegionSearchIndexSnapshot(0, processingProfileId, [], []);
            }

            rootId = rootReader.GetInt64(0);
            scanId = rootReader.IsDBNull(1) ? 0 : rootReader.GetInt64(1);
        }

        using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = """
            SELECT count(*) FROM Images i
            JOIN ImageFingerprints f ON f.ImageId=i.Id
                AND f.AlgorithmId='multiregion' AND f.BitLength=$bits
            WHERE i.RootId=$root AND i.IsActive=1 AND i.ProcessingProfileId=$profile;
            """;
        countCommand.Parameters.AddWithValue("$bits", MultiRegionHash576.RegionCount * 64);
        countCommand.Parameters.AddWithValue("$root", rootId);
        countCommand.Parameters.AddWithValue("$profile", processingProfileId);
        int count = Convert.ToInt32(countCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        long[] imageIds = new long[count];
        byte[] hashes = new byte[checked(count * MultiRegionHash576.ByteLength)];

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT i.Id,f.HashBytes FROM Images i
            JOIN ImageFingerprints f ON f.ImageId=i.Id
                AND f.AlgorithmId='multiregion' AND f.BitLength=$bits
            WHERE i.RootId=$root AND i.IsActive=1 AND i.ProcessingProfileId=$profile ORDER BY i.Id;
            """;
        command.Parameters.AddWithValue("$bits", MultiRegionHash576.RegionCount * 64);
        command.Parameters.AddWithValue("$root", rootId);
        command.Parameters.AddWithValue("$profile", processingProfileId);
        using SqliteDataReader reader = command.ExecuteReader();
        int index = 0;
        while (reader.Read())
        {
            imageIds[index] = reader.GetInt64(0);
            int offset = checked(index * MultiRegionHash576.ByteLength);
            if (reader.GetBytes(1, 0, null, 0, 0) != MultiRegionHash576.ByteLength ||
                reader.GetBytes(1, 0, hashes, offset, MultiRegionHash576.ByteLength) !=
                MultiRegionHash576.ByteLength)
            {
                throw new InvalidDataException("A multi-region fingerprint has an invalid byte length in the index.");
            }

            index++;
        }

        transaction.Commit();
        return new MultiRegionSearchIndexSnapshot(scanId, processingProfileId, imageIds, hashes);
    }

    private static ulong ReadHash64(SqliteDataReader reader, int ordinal, byte[] buffer)
    {
        if (reader.GetBytes(ordinal, 0, null, 0, 0) != buffer.Length ||
            reader.GetBytes(ordinal, 0, buffer, 0, buffer.Length) != buffer.Length)
        {
            throw new InvalidDataException("A 64-bit fingerprint has an invalid byte length in the index.");
        }

        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private SqliteConnection Open()
    {
        if (Path.Exists(_databasePath + ".refresh-required"))
            throw new InvalidOperationException("Indexul necesită reindexarea bibliotecilor afectate de arhivare/recuperare.");
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 60
        };
        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }
}
