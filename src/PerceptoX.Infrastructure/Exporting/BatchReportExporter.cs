using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using PerceptoX.Infrastructure.Maintenance;

namespace PerceptoX.Infrastructure.Exporting;

public sealed record BatchReportCandidate(string Path, string? ThumbnailPath, double Score);
public sealed record BatchReportRow(string QueryPath, string? QueryThumbnailPath, string Status,
    string? Error, IReadOnlyList<BatchReportCandidate> Candidates);
public sealed record BatchReport(string Summary, IReadOnlyList<BatchReportRow> Rows);

public static class BatchReportExporter
{
    public static string Export(BatchReport report, string destination, bool html,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        string id = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(root, $".perceptox-report-{id}.tmp");
        string published = Path.Combine(root, $"PerceptoX-{DateTime.Now:yyyyMMdd-HHmmss}-{id[..8]}");
        Directory.CreateDirectory(temporary);
        string fileName = html ? "report.html" : "report.csv";
        try
        {
            using (StreamWriter writer = new(Path.Combine(temporary, fileName), false, new UTF8Encoding(true)))
            {
                if (html) WriteHtml(writer, report, temporary, cancellationToken);
                else WriteCsv(writer, report, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(temporary, GeneratedArtifacts.ReportMarker), GeneratedArtifacts.ReportSignature);
            Directory.Move(temporary, published);
            return Path.Combine(published, fileName);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static void WriteCsv(TextWriter writer, BatchReport report, CancellationToken token)
    {
        writer.WriteLine("kind,query,status,candidate_rank,matched_path,score_percent,error,summary");
        WriteCells(writer, "summary", "", "", "", "", "", "", report.Summary);
        foreach (BatchReportRow row in report.Rows)
        {
            token.ThrowIfCancellationRequested();
            if (row.Candidates.Count == 0)
                WriteCells(writer, "query", row.QueryPath, row.Status, "", "", "", row.Error ?? "", "");
            for (int i = 0; i < row.Candidates.Count; i++)
            {
                BatchReportCandidate candidate = row.Candidates[i];
                WriteCells(writer, "candidate", row.QueryPath, row.Status,
                    (i + 1).ToString(CultureInfo.InvariantCulture), candidate.Path,
                    candidate.Score.ToString("F2", CultureInfo.InvariantCulture), row.Error ?? "", "");
            }
        }
    }

    private static void WriteCells(TextWriter writer, params string[] cells) =>
        writer.WriteLine(string.Join(',', cells.Select(SearchReportExporter.EscapeCsvCell)));

    private static void WriteHtml(TextWriter writer, BatchReport report, string directory, CancellationToken token)
    {
        writer.Write("""
            <!doctype html><html lang="ro"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>PerceptoX · Raport de căutare</title>
            <style>body{font:15px system-ui;background:#101827;color:#e5eaf3;margin:32px}h1{font-size:28px}article{background:#1c283b;border:1px solid #334155;border-radius:16px;padding:20px;margin:20px 0}.images{display:flex;gap:20px;flex-wrap:wrap}figure{margin:0;width:240px;overflow-wrap:anywhere}img{width:240px;height:180px;object-fit:contain;background:#0e1522;border-radius:10px}strong{color:#85d9f5}small{display:block;color:#bdc9da}.status{color:#85d9f5}p{overflow-wrap:anywhere}</style>
            </head><body><h1>PerceptoX · Raport de căutare</h1>
            """);
        writer.Write($"<p>{Encode(report.Summary)}</p><p>Scorurile măsoară similaritatea; nu reprezintă probabilități. Verificați vizual candidații.</p>");
        string assets = Path.Combine(directory, "assets");
        Directory.CreateDirectory(assets);
        int assetId = 0;
        foreach (BatchReportRow row in report.Rows)
        {
            token.ThrowIfCancellationRequested();
            writer.Write($"<article><h2>{Encode(Path.GetFileName(row.QueryPath))}</h2><p class=\"status\">{Encode(row.Status)}</p><div class=\"images\">");
            Figure(row.QueryPath, row.QueryThumbnailPath, "Referință");
            foreach (BatchReportCandidate candidate in row.Candidates)
            {
                token.ThrowIfCancellationRequested();
                Figure(candidate.Path, candidate.ThumbnailPath, $"Similaritate {candidate.Score:F2}%");
            }
            writer.Write($"</div><p>{Encode(row.Error ?? "")}</p></article>");
        }
        writer.Write("</body></html>");

        void Figure(string path, string? thumbnail, string label)
        {
            writer.Write("<figure>");
            if (thumbnail is not null && File.Exists(thumbnail))
            {
                string name = $"{++assetId}.jpg";
                File.Copy(thumbnail, Path.Combine(assets, name), false);
                writer.Write($"<img loading=\"lazy\" alt=\"{Encode(label)}\" src=\"assets/{name}\">");
            }
            else writer.Write("<p>Previzualizare indisponibilă</p>");
            writer.Write($"<figcaption><strong>{Encode(Path.GetFileName(path))}</strong><small>{Encode(label)}</small><small>{Encode(path)}</small></figcaption></figure>");
        }
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
