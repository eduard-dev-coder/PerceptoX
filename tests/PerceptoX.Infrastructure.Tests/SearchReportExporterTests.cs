using PerceptoX.Application.Matching;
using PerceptoX.Core.Images;
using PerceptoX.Infrastructure.Exporting;

namespace PerceptoX.Infrastructure.Tests;

public sealed class SearchReportExporterTests
{
    [Fact]
    public void CsvEscapesRfc4180CharactersAndNeutralizesFormulas()
    {
        Assert.Equal("\"a,b\"", SearchReportExporter.EscapeCsvCell("a,b"));
        Assert.Equal("\"a\"\"b\"", SearchReportExporter.EscapeCsvCell("a\"b"));
        Assert.Equal("\"\t=HYPERLINK(\"\"x\"\")\"",
            SearchReportExporter.EscapeCsvCell("=HYPERLINK(\"x\")"));
        Assert.Equal("\"\t  +SUM(1,2)\"", SearchReportExporter.EscapeCsvCell("  +SUM(1,2)"));
    }

    [Fact]
    public void CsvIsPublishedWithoutOverwriting()
    {
        string directory = CreateTestDirectory();
        try
        {
            string output = Path.Combine(directory, "results.csv");
            SearchReportExporter.ExportCsv(CreateReport(null), output);
            string csv = File.ReadAllText(output);
            Assert.Contains("score_percent", csv, StringComparison.Ordinal);
            Assert.Contains("\"D:\\Photos\\a,b.jpg\"", csv, StringComparison.Ordinal);
            Assert.Throws<IOException>(() => SearchReportExporter.ExportCsv(CreateReport(null), output));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void HtmlEncodesPathsAndCopiesOnlyCacheThumbnails()
    {
        string directory = CreateTestDirectory();
        try
        {
            string cache = Path.Combine(directory, "cache");
            Directory.CreateDirectory(Path.Combine(cache, "AB"));
            File.WriteAllBytes(Path.Combine(cache, "AB", "1.jpg"), [1, 2, 3]);
            ThumbnailRecord thumbnail = new(1, "v1", "thumb-v1", "AB/1.jpg", "jpeg", 10, 10);
            string output = Path.Combine(directory, "report");

            SearchReportExporter.ExportHtml(CreateReport(thumbnail, "D:\\Photos\\<img src=x onerror=alert(1)>.jpg"),
                cache, output);

            string html = File.ReadAllText(Path.Combine(output, "report.html"));
            Assert.DoesNotContain("<img src=x onerror=alert(1)>.jpg", html, StringComparison.Ordinal);
            Assert.Contains("&lt;img", html, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output, "assets", "1.jpg")));
            Assert.Throws<IOException>(() => SearchReportExporter.ExportHtml(CreateReport(thumbnail), cache, output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PreCancelledExportPublishesNothing()
    {
        string directory = CreateTestDirectory();
        try
        {
            string output = Path.Combine(directory, "results.csv");
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                SearchReportExporter.ExportCsv(CreateReport(null), output, cancellation.Token));
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SearchReport CreateReport(ThumbnailRecord? thumbnail, string path = "D:\\Photos\\a,b.jpg")
    {
        ImageRecord image = new(1, 1, "key", path, 10, 10, 3,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SearchImageDetail detail = new(image, thumbnail);
        return new SearchReport(1, "test", [new SearchReportEntry(detail, 0, 0, 100, true)]);
    }

    private static string CreateTestDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, ".test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
