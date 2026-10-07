namespace PerceptoX.Infrastructure.Exporting;

public sealed record FileCopyResult(int Copied, bool Cancelled, IReadOnlyList<string> Errors);
public sealed record CopySource(string Path, long FileSize, long LastWriteTicks);

/// <summary>Copies originals without overwriting; only completed files get a final name.</summary>
public static class MatchedFileCopier
{
    public static async Task<FileCopyResult> CopyAsync(IEnumerable<string> sourcePaths, string destination,
        CancellationToken cancellationToken = default)
        => await CopyVerifiedAsync(sourcePaths.Select(path =>
        {
            FileInfo info = new(Path.GetFullPath(path));
            return new CopySource(info.FullName, info.Exists ? info.Length : -1, info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
        }), destination, cancellationToken).ConfigureAwait(false);

    public static async Task<FileCopyResult> CopyVerifiedAsync(IEnumerable<CopySource> sources, string destination,
        CancellationToken cancellationToken = default)
        => await CopyCoreAsync(sources, destination, true, cancellationToken).ConfigureAwait(false);

    /// <summary>For a source with proven unique database identities; does not retain every path in memory.</summary>
    public static Task<FileCopyResult> CopyUniqueVerifiedAsync(IEnumerable<CopySource> sources, string destination,
        CancellationToken cancellationToken = default) => CopyCoreAsync(sources, destination, false, cancellationToken);

    private static async Task<FileCopyResult> CopyCoreAsync(IEnumerable<CopySource> sources, string destination,
        bool deduplicate, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(destination);
        PerceptoX.Infrastructure.Maintenance.ManagedPaths.RejectLinks(root);
        Directory.CreateDirectory(root);
        int copied = 0;
        List<string> errors = [];
        HashSet<string>? copiedPaths = deduplicate ? new(StringComparer.OrdinalIgnoreCase) : null;
        foreach (CopySource entry in sources)
        {
            string source = Path.GetFullPath(entry.Path);
            if (copiedPaths is not null && !copiedPaths.Add(source)) continue;
            if (cancellationToken.IsCancellationRequested)
                return new(copied, true, errors);
            string temporary = Path.Combine(root, $".perceptox-{Guid.NewGuid():N}.tmp");
            try
            {
                PerceptoX.Infrastructure.Maintenance.ManagedPaths.RejectLinks(source);
                ValidateVersion(entry);
                await using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    if (input.Length != entry.FileSize || output.Length != entry.FileSize)
                        throw new IOException("Source changed during copying.");
                    ValidateVersion(entry);
                }
                cancellationToken.ThrowIfCancellationRequested();
                for (int suffix = 0; ; suffix++)
                {
                    string name = suffix == 0 ? Path.GetFileName(source)
                        : $"{Path.GetFileNameWithoutExtension(source)} ({suffix}){Path.GetExtension(source)}";
                    string target = Path.Combine(root, name);
                    try
                    {
                        File.Move(temporary, target, overwrite: false);
                        copied++;
                        break;
                    }
                    catch (IOException) when (File.Exists(target) || Directory.Exists(target))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new(copied, true, errors);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{source}: {exception.Message}");
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        return new(copied, false, errors);
    }

    private static void ValidateVersion(CopySource source)
    {
        FileInfo info = new(source.Path);
        if (!info.Exists || info.Length != source.FileSize || info.LastWriteTimeUtc.Ticks != source.LastWriteTicks)
            throw new IOException("Source changed since indexing; scan again before copying.");
    }
}
