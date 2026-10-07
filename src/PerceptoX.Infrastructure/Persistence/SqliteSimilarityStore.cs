using System.Text.Json;
using Microsoft.Data.Sqlite;
using PerceptoX.Application.Matching;

namespace PerceptoX.Infrastructure.Persistence;

public sealed record GroupingRun(string Id, int ImageCount, int GroupCount, int MemberCount,
    double Threshold, int Rejected, int Failed, IReadOnlyList<GroupingRootGeneration> Roots)
{
    public int ExcludedImages { get; init; }
}
public sealed record GroupingStoredMember(int GroupNumber, long ImageId, bool IsRepresentative,
    double Score, bool BinaryIdentical, string FilePath, int Width, int Height, long FileSize,
    long LastWriteTicks, string? ThumbnailPath, int GroupSize);

/// <summary>Feature schema v2. Transactional publication; readers page metadata, not all original images.</summary>
public sealed class SqliteSimilarityStore(string databasePath)
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public GroupingRun Publish(GroupingIndexSnapshot snapshot, IReadOnlyList<SimilarityGroup> groups,
        double threshold, int rejected, int failed, CancellationToken token = default)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureGenerations(connection, transaction, snapshot.Roots);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SimilarityFeatureVersion(Version INTEGER NOT NULL);
            INSERT INTO SimilarityFeatureVersion SELECT 2 WHERE NOT EXISTS(SELECT 1 FROM SimilarityFeatureVersion);
            CREATE TABLE IF NOT EXISTS SimilarityRuns(
                Id TEXT PRIMARY KEY,CreatedUtc TEXT NOT NULL,Threshold REAL NOT NULL,Profile TEXT NOT NULL,
                RootsJson TEXT NOT NULL,ImageCount INTEGER NOT NULL,GroupCount INTEGER NOT NULL,
                MemberCount INTEGER NOT NULL,Rejected INTEGER NOT NULL,Failed INTEGER NOT NULL,
                Excluded INTEGER NOT NULL DEFAULT 0) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS SimilarityMembers(
                RunId TEXT NOT NULL,GroupNumber INTEGER NOT NULL,ImageId INTEGER NOT NULL,
                IsRepresentative INTEGER NOT NULL,Score REAL NOT NULL,BinaryIdentical INTEGER NOT NULL,
                FileSize INTEGER NOT NULL,LastWriteTicks INTEGER NOT NULL,
                PRIMARY KEY(RunId,GroupNumber,ImageId)) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS SimilarityMembers_Image ON SimilarityMembers(RunId,ImageId);
            CREATE TABLE IF NOT EXISTS SimilarityGroups(
                RunId TEXT NOT NULL,GroupNumber INTEGER NOT NULL,MemberCount INTEGER NOT NULL,
                PRIMARY KEY(RunId,GroupNumber)) WITHOUT ROWID;
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT Version FROM SimilarityFeatureVersion;";
        int version = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (version == 1)
        {
            command.CommandText = "ALTER TABLE SimilarityRuns ADD COLUMN Excluded INTEGER NOT NULL DEFAULT 0; UPDATE SimilarityFeatureVersion SET Version=2;";
            command.ExecuteNonQuery(); version = 2;
        }
        if (version != 2)
            throw new InvalidDataException("Unsupported similarity schema.");
        string id = Guid.NewGuid().ToString("N");
        int members = groups.Sum(g => g.Members.Count);
        command.CommandText = "INSERT INTO SimilarityRuns VALUES($id,$time,$threshold,$profile,$roots,$images,$groups,$members,$rejected,$failed,$excluded);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$threshold", threshold);
        command.Parameters.AddWithValue("$profile", snapshot.Fingerprints.ProcessingProfileId + "|group-v2-p3-or-d3-rgb32-rms-score");
        command.Parameters.AddWithValue("$roots", JsonSerializer.Serialize(snapshot.Roots));
        command.Parameters.AddWithValue("$images", snapshot.Fingerprints.Count);
        command.Parameters.AddWithValue("$groups", groups.Count);
        command.Parameters.AddWithValue("$members", members);
        command.Parameters.AddWithValue("$rejected", rejected);
        command.Parameters.AddWithValue("$failed", failed);
        command.Parameters.AddWithValue("$excluded", snapshot.ExcludedImages);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        using var groupCommand = connection.CreateCommand(); groupCommand.Transaction = transaction;
        groupCommand.CommandText = "INSERT INTO SimilarityGroups VALUES($run,$group,$count);";
        groupCommand.Parameters.AddWithValue("$run", id);
        groupCommand.Parameters.AddWithValue("$group", 0); groupCommand.Parameters.AddWithValue("$count", 0);
        command.CommandText = """
            INSERT INTO SimilarityMembers
            SELECT $run,$group,Id,$representative,$score,$binary,FileSize,LastWriteUtcTicks FROM Images WHERE Id=$image AND IsActive=1;
            """;
        command.Parameters.AddWithValue("$run", id);
        foreach (string name in new[] { "$group", "$image", "$representative", "$score", "$binary" }) command.Parameters.AddWithValue(name, 0);
        for (int group = 0; group < groups.Count; group++)
        {
            groupCommand.Parameters["$group"].Value = group + 1;
            groupCommand.Parameters["$count"].Value = groups[group].Members.Count;
            groupCommand.ExecuteNonQuery();
            foreach (var member in groups[group].Members)
            {
                token.ThrowIfCancellationRequested();
                command.Parameters["$group"].Value = group + 1;
                command.Parameters["$image"].Value = member.ImageId;
                command.Parameters["$representative"].Value = member.ImageId == groups[group].RepresentativeId ? 1 : 0;
                command.Parameters["$score"].Value = member.ScorePercent;
                command.Parameters["$binary"].Value = member.BinaryIdentical ? 1 : 0;
                if (command.ExecuteNonQuery() != 1) throw new IOException("Image changed before group publication.");
            }
        }
        token.ThrowIfCancellationRequested();
        // Keep only the latest complete result: no unbounded accumulation of member tables.
        command.Parameters.Clear(); command.Parameters.AddWithValue("$id", id);
        command.CommandText = "DELETE FROM SimilarityMembers WHERE RunId<>$id; DELETE FROM SimilarityGroups WHERE RunId<>$id; DELETE FROM SimilarityRuns WHERE Id<>$id;";
        command.ExecuteNonQuery();
        transaction.Commit();
        return new(id, snapshot.Fingerprints.Count, groups.Count, members, threshold, rejected, failed, snapshot.Roots)
            { ExcludedImages = snapshot.ExcludedImages };
    }

    public void EnsureCurrent(GroupingRun run)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        EnsureGenerations(connection, transaction, run.Roots);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM SimilarityRuns WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", run.Id);
        if ((long)command.ExecuteScalar()! != 1) throw new InvalidOperationException("Similarity results expired; analyze again.");
    }

    private static void EnsureGenerations(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<GroupingRootGeneration> roots)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT LastCompletedScanId FROM LibraryRoots WHERE Id=$root;";
        command.Parameters.Add("$root", SqliteType.Integer);
        foreach (var root in roots)
        {
            command.Parameters[0].Value = root.RootId;
            if (command.ExecuteScalar() is not long scan || scan != root.ScanId)
                throw new InvalidOperationException("Index generation changed; analyze again.");
        }
    }

    public IReadOnlyList<GroupingStoredMember> ReadPage(GroupingRun run, int offset, int count,
        int? groupNumber = null, bool representativesOnly = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 1000);
        EnsureCurrent(run);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.GroupNumber,m.ImageId,m.IsRepresentative,m.Score,m.BinaryIdentical,
                i.FilePath,i.Width,i.Height,m.FileSize,m.LastWriteTicks,t.RelativePath,
                g.MemberCount
            FROM SimilarityMembers m JOIN Images i ON i.Id=m.ImageId
            JOIN SimilarityGroups g ON g.RunId=m.RunId AND g.GroupNumber=m.GroupNumber
            LEFT JOIN Thumbnails t ON t.ImageId=m.ImageId
            WHERE m.RunId=$run AND ($group IS NULL OR m.GroupNumber=$group) AND ($reps=0 OR m.IsRepresentative=1)
            ORDER BY m.GroupNumber,m.IsRepresentative DESC,m.ImageId LIMIT $count OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$run", run.Id);
        command.Parameters.AddWithValue("$group", groupNumber is int group ? group : DBNull.Value);
        command.Parameters.AddWithValue("$reps", representativesOnly ? 1 : 0);
        command.Parameters.AddWithValue("$count", count); command.Parameters.AddWithValue("$offset", offset);
        List<GroupingStoredMember> result = [];
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2) != 0,
            reader.GetDouble(3), reader.GetInt32(4) != 0, reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7),
            reader.GetInt64(8), reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetInt32(11)));
        return result;
    }

    public IEnumerable<GroupingStoredMember> EnumerateMembers(GroupingRun run, CancellationToken token = default)
    {
        EnsureCurrent(run);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.GroupNumber,m.ImageId,m.IsRepresentative,m.Score,m.BinaryIdentical,
                i.FilePath,i.Width,i.Height,m.FileSize,m.LastWriteTicks,t.RelativePath,g.MemberCount
            FROM SimilarityMembers m JOIN Images i ON i.Id=m.ImageId
            JOIN SimilarityGroups g ON g.RunId=m.RunId AND g.GroupNumber=m.GroupNumber
            LEFT JOIN Thumbnails t ON t.ImageId=m.ImageId WHERE m.RunId=$run
            ORDER BY m.GroupNumber,m.IsRepresentative DESC,m.ImageId;
            """;
        command.Parameters.AddWithValue("$run", run.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            yield return new(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2) != 0,
                reader.GetDouble(3), reader.GetInt32(4) != 0, reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt64(8), reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetInt32(11));
        }
    }
}
