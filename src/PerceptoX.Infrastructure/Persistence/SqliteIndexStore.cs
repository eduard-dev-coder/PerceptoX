using Microsoft.Data.Sqlite;
using PerceptoX.Application.Indexing;
using PerceptoX.Core.Fingerprints;
using PerceptoX.Infrastructure.Indexing;
using PerceptoX.Infrastructure.Maintenance;

namespace PerceptoX.Infrastructure.Persistence;

internal sealed class SqliteIndexStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FileStream _scanLock;
    private readonly object _gate = new();
    private readonly WorkspaceLease _workspaceLease;
    private string? _libraryRoot;
    private long _rootId;
    private long _scanId;

    private SqliteIndexStore(SqliteConnection connection, FileStream scanLock, WorkspaceLease workspaceLease)
    {
        _connection = connection;
        _scanLock = scanLock;
        _workspaceLease = workspaceLease;
    }

    public long ScanId => _scanId;

    public static SqliteIndexStore Open(IndexingRequest request)
    {
        string? directory = Path.GetDirectoryName(request.DatabasePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        WorkspaceLease workspaceLease = WorkspaceLease.Acquire(request.DatabasePath, request.ThumbnailCacheRoot);
        FileStream? scanLock = null;
        SqliteConnection? connection = null;
        try
        {
            if (!File.Exists(request.DatabasePath) &&
                (File.Exists(request.DatabasePath + "-wal") || File.Exists(request.DatabasePath + "-shm")))
                throw new IOException("Resetarea indexului este incompletă. Reluați resetarea înainte de indexare.");
            scanLock = new(request.DatabasePath + ".scan.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = request.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                DefaultTimeout = 60
            };
            connection = new SqliteConnection(builder.ToString());
            connection.Open();
            Execute(connection, "PRAGMA journal_mode=WAL;");
            Execute(connection, "PRAGMA synchronous=NORMAL;");
            Execute(connection, "PRAGMA foreign_keys=ON;");
            Migrate(connection);
            return new SqliteIndexStore(connection, scanLock, workspaceLease);
        }
        catch
        {
            connection?.Dispose();
            scanLock?.Dispose();
            workspaceLease.Dispose();
            throw;
        }
    }

    public void BeginScan(IndexingRequest request)
    {
        lock (_gate)
        {
            if (_scanId != 0)
            {
                throw new InvalidOperationException("This store already has an active scan.");
            }

            using SqliteTransaction transaction = _connection.BeginTransaction();
            _libraryRoot = request.LibraryRoot;
            Execute(_connection, "UPDATE ScanRuns SET Status='abandoned', FinishedUtcTicks=$now WHERE Status='running';",
                transaction, ("$now", DateTime.UtcNow.Ticks));
            Execute(_connection, "DELETE FROM PendingFingerprints WHERE ScanId IN (SELECT Id FROM ScanRuns WHERE Status='abandoned');", transaction);
            Execute(_connection, "DELETE FROM PendingThumbnails WHERE ScanId IN (SELECT Id FROM ScanRuns WHERE Status='abandoned');", transaction);
            Execute(_connection, "DELETE FROM PendingImages WHERE ScanId IN (SELECT Id FROM ScanRuns WHERE Status='abandoned');", transaction);
            Execute(_connection, "DELETE FROM ScanSeen WHERE ScanId IN (SELECT Id FROM ScanRuns WHERE Status='abandoned');", transaction);

            Execute(_connection, "INSERT INTO LibraryRoots(RootKey, RootPath, PathKeyVersion) VALUES($key,$path,$version) ON CONFLICT(RootKey) DO UPDATE SET RootPath=excluded.RootPath;",
                transaction, ("$key", WindowsPathKey.Create(request.LibraryRoot)), ("$path", request.LibraryRoot), ("$version", WindowsPathKey.Version));
            _rootId = ScalarLong(_connection, "SELECT Id FROM LibraryRoots WHERE RootKey=$key;", transaction,
                ("$key", WindowsPathKey.Create(request.LibraryRoot)));
            Execute(_connection, "INSERT INTO ScanRuns(RootId, Status, StartedUtcTicks,DiscoveryComplete) VALUES($root,'running',$now,0);",
                transaction, ("$root", _rootId), ("$now", DateTime.UtcNow.Ticks));
            _scanId = ScalarLong(_connection, "SELECT last_insert_rowid();", transaction);
            transaction.Commit();
        }
    }

    public IReadOnlyList<PreparedFile> PrepareBatch(IReadOnlyList<ScanFile> files, string profileId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureScan();
            cancellationToken.ThrowIfCancellationRequested();
            List<PreparedFile> prepared = new(files.Count);
            using SqliteTransaction transaction = _connection.BeginTransaction();
            foreach (ScanFile file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Execute(_connection,
                    "INSERT INTO ImageIdentities(RootId,PathKey) VALUES($root,$key) ON CONFLICT(RootId,PathKey) DO NOTHING;",
                    transaction, ("$root", _rootId), ("$key", file.PathKey));
                long imageId = ScalarLong(_connection,
                    "SELECT Id FROM ImageIdentities WHERE RootId=$root AND PathKey=$key;",
                    transaction, ("$root", _rootId), ("$key", file.PathKey));
                Execute(_connection, "INSERT INTO ScanSeen(ScanId,ImageId) VALUES($scan,$image);",
                    transaction, ("$scan", _scanId), ("$image", imageId));

                using SqliteCommand status = CreateCommand(_connection,
                    "SELECT i.FileSize,i.LastWriteUtcTicks,i.ProcessingProfileId,i.IsActive,t.RelativePath " +
                    "FROM Images i LEFT JOIN Thumbnails t ON t.ImageId=i.Id WHERE i.Id=$image;",
                    transaction, ("$image", imageId));
                using SqliteDataReader reader = status.ExecuteReader();
                bool unchanged = reader.Read() && reader.GetInt64(0) == file.Size &&
                    reader.GetInt64(1) == file.LastWriteUtcTicks &&
                    reader.GetString(2) == profileId && reader.GetInt64(3) == 1 &&
                    !reader.IsDBNull(4);
                prepared.Add(new PreparedFile(imageId, file, unchanged));
            }

            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return prepared;
        }
    }

    public void StageBatch(IReadOnlyList<ProcessedFile> files, string profileId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureScan();
            cancellationToken.ThrowIfCancellationRequested();
            using SqliteTransaction transaction = _connection.BeginTransaction();
            foreach (ProcessedFile file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Result is null)
                {
                    Execute(_connection,
                        "INSERT INTO ScanErrors(ScanId,ImageId,ErrorCode) VALUES($scan,$image,$error);",
                        transaction, ("$scan", _scanId), ("$image", file.Prepared.ImageId),
                        ("$error", file.ErrorCode ?? "Unknown"));
                    continue;
                }

                ScanFile source = file.Prepared.File;
                long imageId = file.Prepared.ImageId;
                Execute(_connection,
                    "INSERT INTO PendingImages(ScanId,ImageId,FilePath,FileSize,LastWriteUtcTicks,Width,Height,SourceVersion,ProcessingProfileId) " +
                    "VALUES($scan,$image,$path,$size,$ticks,$width,$height,$source,$profile);",
                    transaction, ("$scan", _scanId), ("$image", imageId), ("$path", source.Path),
                    ("$size", source.Size), ("$ticks", source.LastWriteUtcTicks),
                    ("$width", file.Result.Fingerprints.Width), ("$height", file.Result.Fingerprints.Height),
                    ("$source", source.SourceVersion), ("$profile", profileId));
                foreach (Fingerprint fingerprint in file.Result.Fingerprints.Fingerprints.Values)
                {
                    FingerprintDescriptor descriptor = fingerprint.Descriptor;
                    Execute(_connection,
                        "INSERT INTO PendingFingerprints(ScanId,ImageId,AlgorithmId,AlgorithmVersion,ProfileId,BitLength,SampleWidth,SampleHeight,HashBytes) " +
                        "VALUES($scan,$image,$algorithm,$version,$profile,$bits,$width,$height,$hash);",
                        transaction, ("$scan", _scanId), ("$image", imageId),
                        ("$algorithm", descriptor.AlgorithmId), ("$version", descriptor.AlgorithmVersion),
                        ("$profile", descriptor.ProfileId), ("$bits", descriptor.BitLength),
                        ("$width", descriptor.SampleWidth), ("$height", descriptor.SampleHeight),
                        ("$hash", fingerprint.Value.Bytes.ToArray()));
                }

                var thumbnail = file.Result.Thumbnail;
                if (thumbnail.ImageId != imageId || thumbnail.SourceVersion != source.SourceVersion)
                {
                    throw new InvalidDataException("The processor returned a thumbnail for another image or source version.");
                }

                Execute(_connection,
                    "INSERT INTO PendingThumbnails(ScanId,ImageId,SourceVersion,ProfileId,RelativePath,FormatId,Width,Height) " +
                    "VALUES($scan,$image,$source,$profile,$path,$format,$width,$height);",
                    transaction, ("$scan", _scanId), ("$image", imageId),
                    ("$source", thumbnail.SourceVersion), ("$profile", thumbnail.ProfileId),
                    ("$path", thumbnail.RelativePath), ("$format", thumbnail.FormatId),
                    ("$width", thumbnail.Width), ("$height", thumbnail.Height));
            }

            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }
    }

    public int Publish(bool discoveryComplete = true, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureScan();
            cancellationToken.ThrowIfCancellationRequested();
            using SqliteTransaction transaction = _connection.BeginTransaction();
            Execute(_connection,
                "INSERT INTO Images(Id,RootId,FilePath,FileSize,LastWriteUtcTicks,Width,Height,SourceVersion,ProcessingProfileId,IsActive) " +
                "SELECT ImageId,$root,FilePath,FileSize,LastWriteUtcTicks,Width,Height,SourceVersion,ProcessingProfileId,1 " +
                "FROM PendingImages WHERE ScanId=$scan AND true " +
                "ON CONFLICT(Id) DO UPDATE SET FilePath=excluded.FilePath,FileSize=excluded.FileSize," +
                "LastWriteUtcTicks=excluded.LastWriteUtcTicks,Width=excluded.Width,Height=excluded.Height," +
                "SourceVersion=excluded.SourceVersion,ProcessingProfileId=excluded.ProcessingProfileId,IsActive=1;",
                transaction, ("$root", _rootId), ("$scan", _scanId));
            Execute(_connection,
                "DELETE FROM ImageFingerprints WHERE ImageId IN (SELECT ImageId FROM PendingImages WHERE ScanId=$scan);",
                transaction, ("$scan", _scanId));
            Execute(_connection,
                "INSERT INTO ImageFingerprints(ImageId,AlgorithmId,AlgorithmVersion,ProfileId,BitLength,SampleWidth,SampleHeight,HashBytes) " +
                "SELECT ImageId,AlgorithmId,AlgorithmVersion,ProfileId,BitLength,SampleWidth,SampleHeight,HashBytes " +
                "FROM PendingFingerprints WHERE ScanId=$scan;", transaction, ("$scan", _scanId));
            Execute(_connection,
                "INSERT INTO Thumbnails(ImageId,SourceVersion,ProfileId,RelativePath,FormatId,Width,Height) " +
                "SELECT ImageId,SourceVersion,ProfileId,RelativePath,FormatId,Width,Height " +
                "FROM PendingThumbnails WHERE ScanId=$scan AND true " +
                "ON CONFLICT(ImageId) DO UPDATE SET SourceVersion=excluded.SourceVersion,ProfileId=excluded.ProfileId," +
                "RelativePath=excluded.RelativePath,FormatId=excluded.FormatId,Width=excluded.Width,Height=excluded.Height;",
                transaction, ("$scan", _scanId));
            int deactivated = discoveryComplete ? Execute(_connection,
                "UPDATE Images SET IsActive=0 WHERE RootId=$root AND IsActive=1 " +
                "AND NOT EXISTS(SELECT 1 FROM ScanSeen s WHERE s.ScanId=$scan AND s.ImageId=Images.Id);",
                transaction, ("$root", _rootId), ("$scan", _scanId)) : 0;
            ClearStage(transaction);
            Execute(_connection, "UPDATE ScanRuns SET Status='completed',DiscoveryComplete=$complete,FinishedUtcTicks=$now WHERE Id=$scan;",
                transaction, ("$complete", discoveryComplete ? 1 : 0), ("$now", DateTime.UtcNow.Ticks), ("$scan", _scanId));
            if (discoveryComplete) Execute(_connection, "UPDATE LibraryRoots SET LastCompletedScanId=$scan WHERE Id=$root;",
                transaction, ("$scan", _scanId), ("$root", _rootId));
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            _scanId = 0;
            if (discoveryComplete) IndexRefreshGuard.CompletedScan(_connection.DataSource, _libraryRoot!);
            return deactivated;
        }
    }

    public void Abort()
    {
        lock (_gate)
        {
            if (_scanId == 0)
            {
                return;
            }

            using SqliteTransaction transaction = _connection.BeginTransaction();
            ClearStage(transaction);
            Execute(_connection, "UPDATE ScanRuns SET Status='cancelled',FinishedUtcTicks=$now WHERE Id=$scan;",
                transaction, ("$now", DateTime.UtcNow.Ticks), ("$scan", _scanId));
            transaction.Commit();
            _scanId = 0;
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
        _scanLock.Dispose();
        _workspaceLease.Dispose();
    }

    private void ClearStage(SqliteTransaction transaction)
    {
        foreach (string table in new[] { "PendingFingerprints", "PendingThumbnails", "PendingImages", "ScanSeen" })
        {
            Execute(_connection, $"DELETE FROM {table} WHERE ScanId=$scan;", transaction, ("$scan", _scanId));
        }
    }

    private void EnsureScan()
    {
        if (_scanId == 0)
        {
            throw new InvalidOperationException("No scan is active.");
        }
    }

    private static void Migrate(SqliteConnection connection)
    {
        long version = ScalarLong(connection, "PRAGMA user_version;");
        if (version > 2)
        {
            throw new NotSupportedException($"Database schema {version} is newer than this version of PerceptoX.");
        }

        if (version == 2)
        {
            return;
        }

        using SqliteTransaction transaction = connection.BeginTransaction();
        if (version == 1)
        {
            Execute(connection, "ALTER TABLE ScanRuns ADD COLUMN DiscoveryComplete INTEGER NOT NULL DEFAULT 1 CHECK(DiscoveryComplete IN (0,1));", transaction);
            Execute(connection, "UPDATE SchemaInfo SET Version=2; PRAGMA user_version=2;", transaction);
            transaction.Commit();
            return;
        }
        Execute(connection, """
            CREATE TABLE SchemaInfo(Version INTEGER NOT NULL);
            INSERT INTO SchemaInfo(Version) VALUES(2);
            CREATE TABLE LibraryRoots(
                Id INTEGER PRIMARY KEY,
                RootKey TEXT NOT NULL UNIQUE,
                RootPath TEXT NOT NULL,
                PathKeyVersion INTEGER NOT NULL,
                LastCompletedScanId INTEGER);
            CREATE TABLE ScanRuns(
                Id INTEGER PRIMARY KEY,
                RootId INTEGER NOT NULL REFERENCES LibraryRoots(Id),
                Status TEXT NOT NULL CHECK(Status IN ('running','completed','cancelled','abandoned')),
                StartedUtcTicks INTEGER NOT NULL,
                FinishedUtcTicks INTEGER,
                DiscoveryComplete INTEGER NOT NULL DEFAULT 0 CHECK(DiscoveryComplete IN (0,1)));
            CREATE TABLE ImageIdentities(
                Id INTEGER PRIMARY KEY,
                RootId INTEGER NOT NULL REFERENCES LibraryRoots(Id),
                PathKey TEXT NOT NULL,
                UNIQUE(RootId,PathKey));
            CREATE TABLE Images(
                Id INTEGER PRIMARY KEY REFERENCES ImageIdentities(Id),
                RootId INTEGER NOT NULL REFERENCES LibraryRoots(Id),
                FilePath TEXT NOT NULL,
                FileSize INTEGER NOT NULL CHECK(FileSize>=0),
                LastWriteUtcTicks INTEGER NOT NULL,
                Width INTEGER NOT NULL CHECK(Width>0),
                Height INTEGER NOT NULL CHECK(Height>0),
                SourceVersion TEXT NOT NULL,
                ProcessingProfileId TEXT NOT NULL,
                IsActive INTEGER NOT NULL CHECK(IsActive IN (0,1)));
            CREATE INDEX IX_Images_RootActive ON Images(RootId,IsActive);
            CREATE TABLE ImageFingerprints(
                ImageId INTEGER NOT NULL REFERENCES Images(Id) ON DELETE CASCADE,
                AlgorithmId TEXT NOT NULL,
                AlgorithmVersion INTEGER NOT NULL,
                ProfileId TEXT NOT NULL,
                BitLength INTEGER NOT NULL,
                SampleWidth INTEGER NOT NULL,
                SampleHeight INTEGER NOT NULL,
                HashBytes BLOB NOT NULL,
                PRIMARY KEY(ImageId,AlgorithmId,AlgorithmVersion,ProfileId)) WITHOUT ROWID;
            CREATE TABLE Thumbnails(
                ImageId INTEGER PRIMARY KEY REFERENCES Images(Id) ON DELETE CASCADE,
                SourceVersion TEXT NOT NULL,
                ProfileId TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                FormatId TEXT NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL);
            CREATE TABLE ScanSeen(
                ScanId INTEGER NOT NULL REFERENCES ScanRuns(Id),
                ImageId INTEGER NOT NULL REFERENCES ImageIdentities(Id),
                PRIMARY KEY(ScanId,ImageId)) WITHOUT ROWID;
            CREATE TABLE PendingImages(
                ScanId INTEGER NOT NULL REFERENCES ScanRuns(Id),
                ImageId INTEGER NOT NULL REFERENCES ImageIdentities(Id),
                FilePath TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                LastWriteUtcTicks INTEGER NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL,
                SourceVersion TEXT NOT NULL,
                ProcessingProfileId TEXT NOT NULL,
                PRIMARY KEY(ScanId,ImageId)) WITHOUT ROWID;
            CREATE TABLE PendingFingerprints(
                ScanId INTEGER NOT NULL,
                ImageId INTEGER NOT NULL,
                AlgorithmId TEXT NOT NULL,
                AlgorithmVersion INTEGER NOT NULL,
                ProfileId TEXT NOT NULL,
                BitLength INTEGER NOT NULL,
                SampleWidth INTEGER NOT NULL,
                SampleHeight INTEGER NOT NULL,
                HashBytes BLOB NOT NULL,
                PRIMARY KEY(ScanId,ImageId,AlgorithmId,AlgorithmVersion,ProfileId),
                FOREIGN KEY(ScanId,ImageId) REFERENCES PendingImages(ScanId,ImageId) ON DELETE CASCADE) WITHOUT ROWID;
            CREATE TABLE PendingThumbnails(
                ScanId INTEGER NOT NULL,
                ImageId INTEGER NOT NULL,
                SourceVersion TEXT NOT NULL,
                ProfileId TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                FormatId TEXT NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL,
                PRIMARY KEY(ScanId,ImageId),
                FOREIGN KEY(ScanId,ImageId) REFERENCES PendingImages(ScanId,ImageId) ON DELETE CASCADE) WITHOUT ROWID;
            CREATE TABLE ScanErrors(
                ScanId INTEGER NOT NULL REFERENCES ScanRuns(Id),
                ImageId INTEGER NOT NULL REFERENCES ImageIdentities(Id),
                ErrorCode TEXT NOT NULL);
            CREATE TABLE Settings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL) WITHOUT ROWID;
            CREATE TABLE SearchHistory(Id INTEGER PRIMARY KEY, CreatedUtcTicks INTEGER NOT NULL, QueryImageId INTEGER);
            """, transaction);
        Execute(connection, "PRAGMA user_version=2;", transaction);
        transaction.Commit();
    }

    private static int Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = CreateCommand(connection, sql, transaction, parameters);
        return command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = CreateCommand(connection, sql, transaction, parameters);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection, string sql, SqliteTransaction? transaction,
        params (string Name, object Value)[] parameters)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
