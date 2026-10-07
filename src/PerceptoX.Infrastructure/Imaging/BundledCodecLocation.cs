namespace PerceptoX.Infrastructure.Imaging;

/// <summary>App-relative location; no registry, PATH or machine-installed codec lookup.</summary>
public static class BundledCodecLocation
{
    public static string? Find(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        if (!Path.IsPathFullyQualified(applicationDirectory))
            throw new ArgumentException("The application directory must be absolute.", nameof(applicationDirectory));
        string executable = Path.Combine(Path.GetFullPath(applicationDirectory), "codecs", "imagemagick", "magick.exe");
        return File.Exists(executable) ? executable : null;
    }
}
