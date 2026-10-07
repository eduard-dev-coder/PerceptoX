namespace PerceptoX.Infrastructure.Indexing;

internal sealed record ScanFile(string Path, string PathKey, long Size, long LastWriteUtcTicks)
{
    public string SourceVersion => $"{Size}:{LastWriteUtcTicks}";
}

internal sealed record PreparedFile(long ImageId, ScanFile File, bool IsUnchanged);
