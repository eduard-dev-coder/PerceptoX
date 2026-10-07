using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using PerceptoX.Application.Matching;

namespace PerceptoX.Infrastructure.Exporting;

public static class SearchReportExporter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static void ExportCsv(SearchReport report, string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string destination = Path.GetFullPath(outputPath);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("The CSV destination already exists; reports never overwrite data.");
        }

        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}-{Guid.NewGuid():N}.tmp");
        try
        {
            using (StreamWriter writer = new(temporary, append: false, Utf8WithBom))
            {
                writer.WriteLine("kind,image_id,phash_distance,dhash_distance,multiregion_best_distance," +
                    "multiregion_matched_regions,score_percent,width,height,file_size,path");
                foreach (SearchReportEntry entry in report.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string[] cells =
                    [
                        entry.IsQuery ? "query" : "match",
                        entry.Detail.Image.Id.ToString(CultureInfo.InvariantCulture),
                        entry.PerceptualDistance.ToString(CultureInfo.InvariantCulture),
                        entry.DifferenceDistance.ToString(CultureInfo.InvariantCulture),
                        entry.MultiRegionBestDistance?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        entry.MultiRegionMatchedRegions.ToString(CultureInfo.InvariantCulture),
                        entry.ScorePercent.ToString("F4", CultureInfo.InvariantCulture),
                        entry.Detail.Image.Width.ToString(CultureInfo.InvariantCulture),
                        entry.Detail.Image.Height.ToString(CultureInfo.InvariantCulture),
                        entry.Detail.Image.FileSize.ToString(CultureInfo.InvariantCulture),
                        entry.Detail.Image.FilePath
                    ];
                    writer.WriteLine(string.Join(',', cells.Select(EscapeCsvCell)));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static void ExportHtml(SearchReport report, string thumbnailCacheRoot, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailCacheRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(thumbnailCacheRoot));
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("The HTML report directory already exists; reports never overwrite data.");
        }

        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string temporary = Path.Combine(parent, $".{Path.GetFileName(destination)}-{Guid.NewGuid():N}.tmp");
        try
        {
            string assets = Path.Combine(temporary, "assets");
            Directory.CreateDirectory(assets);
            using (StreamWriter writer = new(Path.Combine(temporary, "report.html"), append: false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write("""
                <!doctype html><html lang="ro"><head><meta charset="utf-8">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <title>PerceptoX - rezultate similare</title>
                <style>body{font:15px system-ui;margin:2rem;background:#111827;color:#e5e7eb}h1{font-size:1.5rem}.notice{color:#fbbf24}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:1rem}.card{background:#1f2937;border-radius:10px;padding:1rem;overflow-wrap:anywhere}.card img{display:block;max-width:100%;height:180px;object-fit:contain;margin:auto;background:#0b1020}.score{font-size:1.2rem;font-weight:700}.path{font-family:ui-monospace,monospace;font-size:.8rem}</style>
                </head><body><h1>PerceptoX - rezultate similare</h1>
                <p class="notice">Scorurile sunt necalibrate, nu sunt probabilități și nu autorizează ștergerea fișierelor.</p><div class="grid">
                """);
                foreach (SearchReportEntry entry in report.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? assetName = CopyThumbnail(entry, cacheRoot, assets);
                    writer.Write("<article class=\"card\"><h2>");
                    HtmlEncoder.Default.Encode(writer, entry.IsQuery ? "Imagine selectată" : $"Potrivire #{entry.Detail.Image.Id}");
                    writer.Write("</h2>");
                    if (assetName is not null)
                    {
                        writer.Write("<img loading=\"lazy\" alt=\"Miniatură\" src=\"assets/");
                        HtmlEncoder.Default.Encode(writer, assetName);
                        writer.Write("\">");
                    }

                    writer.Write("<p class=\"score\">");
                    HtmlEncoder.Default.Encode(writer,
                        entry.IsQuery ? "Query" : $"{entry.ScorePercent.ToString("F2", CultureInfo.InvariantCulture)}% (necalibrat)");
                    writer.Write("</p><p>pHash ");
                    writer.Write(entry.PerceptualDistance.ToString(CultureInfo.InvariantCulture));
                    writer.Write("/64 · dHash ");
                    writer.Write(entry.DifferenceDistance.ToString(CultureInfo.InvariantCulture));
                    writer.Write("/64");
                    if (entry.MultiRegionBestDistance is int regionalDistance)
                    {
                        writer.Write(" · multi-region best ");
                        writer.Write(regionalDistance.ToString(CultureInfo.InvariantCulture));
                        writer.Write("/64, regiuni ");
                        writer.Write(entry.MultiRegionMatchedRegions.ToString(CultureInfo.InvariantCulture));
                        writer.Write("/9");
                    }

                    writer.Write("</p><p class=\"path\">");
                    HtmlEncoder.Default.Encode(writer, entry.Detail.Image.FilePath);
                    writer.Write("</p></article>");
                }

                writer.Write("</div></body></html>");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporary, destination);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    internal static string EscapeCsvCell(string value)
    {
        string safe = IsFormula(value) ? "\t" + value : value;
        return '"' + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static bool IsFormula(string value)
    {
        int index = 0;
        while (index < value.Length && value[index] is ' ' or '\t' or '\r' or '\n')
        {
            index++;
        }

        return index < value.Length && value[index] is '=' or '+' or '-' or '@';
    }

    private static string? CopyThumbnail(SearchReportEntry entry, string cacheRoot, string assets)
    {
        if (entry.Detail.Thumbnail is null)
        {
            return null;
        }

        string source = Path.GetFullPath(Path.Combine(cacheRoot,
            entry.Detail.Thumbnail.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!source.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(source))
        {
            return null;
        }

        string name = $"{entry.Detail.Image.Id}.jpg";
        File.Copy(source, Path.Combine(assets, name), overwrite: false);
        return name;
    }
}
