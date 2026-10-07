using System.Text;

namespace PerceptoX.Infrastructure.Indexing;

public static class WindowsPathKey
{
    public const int Version = 1;

    public static string Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return $"win-v{Version}:" + fullPath.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}
