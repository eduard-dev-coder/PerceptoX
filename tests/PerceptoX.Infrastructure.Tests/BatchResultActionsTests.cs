using PerceptoX.Infrastructure.Exporting;

namespace PerceptoX.Infrastructure.Tests;

public sealed class BatchResultActionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PerceptoX-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CopyDeduplicatesPathsAndPreservesAllNameCollisions()
    {
        string first = MakeFile("a/photo.jpg", "first");
        string second = MakeFile("b/photo.jpg", "second");
        string existing = MakeFile("out/photo.jpg", "existing");
        FileCopyResult result = await MatchedFileCopier.CopyAsync([first, first, second], Path.GetDirectoryName(existing)!);
        Assert.Equal(2, result.Copied);
        Assert.Empty(result.Errors);
        Assert.Equal("existing", File.ReadAllText(existing));
        Assert.Equal("first", File.ReadAllText(Path.Combine(_root, "out/photo (1).jpg")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_root, "out/photo (2).jpg")));
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "out"), "*.tmp"));
    }

    [Fact]
    public async Task MissingSourceIsReportedAndRemainingFilesAreCopied()
    {
        string valid = MakeFile("photo.jpg", "valid");
        FileCopyResult result = await MatchedFileCopier.CopyAsync(
            [Path.Combine(_root, "missing.jpg"), valid], Path.Combine(_root, "out"));
        Assert.Equal(1, result.Copied);
        Assert.Single(result.Errors);
        Assert.False(result.Cancelled);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "out"), "*.tmp"));
    }

    [Fact]
    public async Task CancelledCopyDoesNotPublishAnyFile()
    {
        string source = MakeFile("photo.jpg", "data");
        FileCopyResult result = await MatchedFileCopier.CopyAsync([source], Path.Combine(_root, "out"), new CancellationToken(true));
        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Copied);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "out")));
    }

    [Fact]
    public void CsvIncludesAllStatusesCandidatesSummaryAndEscapesUserText()
    {
        BatchReport report = Report();
        string path = BatchReportExporter.Export(report, _root, false);
        string csv = File.ReadAllText(path);
        Assert.Contains("Total: 5", csv, StringComparison.Ordinal);
        Assert.Contains("\t=SUM(1,2)", csv, StringComparison.Ordinal);
        Assert.Contains("\"2\",\"other.jpg\",\"93.50\"", csv, StringComparison.Ordinal);
        foreach (string status in new[] { "Găsită", "Aproape identică", "Incertă", "Negăsită", "Eroare" })
            Assert.Contains(status, csv, StringComparison.Ordinal);
        string second = BatchReportExporter.Export(report, _root, false);
        Assert.NotEqual(path, second);
        Assert.Equal(csv, File.ReadAllText(path));
    }

    [Fact]
    public void HtmlEncodesUserTextAndIncludesPortableReferenceAndCandidateThumbnails()
    {
        string thumbnail = MakeFile("cache/preview.jpg", "thumbnail-bytes");
        BatchReport report = new("Total: 1", [new("<script>alert(1)</script>", thumbnail,
            "Găsită", null, [new("original.jpg", thumbnail, 99)])]);
        string path = BatchReportExporter.Export(report, Path.Combine(_root, "reports"), true);
        string html = File.ReadAllText(path);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("assets/1.jpg", html, StringComparison.Ordinal);
        Assert.Contains("assets/2.jpg", html, StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(Path.GetDirectoryName(path)!, "assets")).Length);
    }

    [Fact]
    public void CancelledReportPublishesNothing()
    {
        Assert.Throws<OperationCanceledException>(() => BatchReportExporter.Export(Report(), _root, false, new CancellationToken(true)));
        Assert.False(Directory.Exists(_root));
    }

    private static BatchReport Report() => new("Total: 5", [
        new("=SUM(1,2)", null, "Găsită", null, [new("original.jpg", null, 97), new("other.jpg", null, 93.5)]),
        new("a.jpg", null, "Aproape identică", null, [new("a-original.jpg", null, 100)]),
        new("b.jpg", null, "Incertă", null, []),
        new("c.jpg", null, "Negăsită", null, []),
        new("d.jpg", null, "Eroare", "Decode failed", [])]);

    private string MakeFile(string relative, string contents)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
