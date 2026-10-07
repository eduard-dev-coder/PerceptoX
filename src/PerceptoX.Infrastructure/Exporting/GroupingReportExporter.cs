using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using PerceptoX.Infrastructure.Maintenance;
using PerceptoX.Infrastructure.Persistence;

namespace PerceptoX.Infrastructure.Exporting;

public sealed record GroupingReportLabels(string Language, string Title, string Representative, string Selected,
    string BinaryIdentical, string CloseVariant, string Yes, string No);

public static class GroupingReportExporter
{
    public static string Export(IEnumerable<GroupingStoredMember> members, string destination,
        string cacheRoot, string summary, bool html, Func<long, bool, bool> selected, GroupingReportLabels labels,
        CancellationToken token = default)
    {
        ManagedPaths.RejectLinks(destination);
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        string id = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(root, ".perceptox-report-" + id + ".tmp");
        string published = Path.Combine(root, "PerceptoX-groups-" + id[..12]);
        Directory.CreateDirectory(temporary);
        string file = html ? "report.html" : "report.csv";
        try
        {
            using (StreamWriter writer = new(Path.Combine(temporary, file), false, new UTF8Encoding(true)))
            {
                if (html)
                {
                    Directory.CreateDirectory(Path.Combine(temporary, "assets"));
                    writer.Write("<!doctype html><html lang=\"" + HtmlEncoder.Default.Encode(labels.Language) + "\"><head><meta charset=\"utf-8\"><title>PerceptoX · " + HtmlEncoder.Default.Encode(labels.Title)
                        + "</title><style>body{font:15px system-ui;background:#111b2b;color:#ecf2ff;margin:24px}section{border:1px solid #33445f;border-radius:12px;padding:16px;margin:16px 0;display:flex;gap:16px;flex-wrap:wrap}h2{width:100%}figure{margin:0;width:240px;overflow-wrap:anywhere}img{width:240px;height:180px;object-fit:contain}strong{color:#78b4ff}small{display:block}</style></head><body><h1>PerceptoX · " + HtmlEncoder.Default.Encode(labels.Title) + "</h1>");
                    writer.Write("<p>" + HtmlEncoder.Default.Encode(summary) + "</p>");
                }
                else
                {
                    writer.WriteLine("group,image_id,representative,selected,score_percent,binary_identical_to_representative,path,width,height,bytes,summary");
                    writer.WriteLine(",,,,,,,,,," + SearchReportExporter.EscapeCsvCell(summary));
                }
                int previous = 0, asset = 0;
                foreach (var member in members)
                {
                    token.ThrowIfCancellationRequested();
                    bool isSelected = selected(member.ImageId, member.IsRepresentative);
                    if (!html)
                    {
                        string[] cells = [member.GroupNumber.ToString(CultureInfo.InvariantCulture), member.ImageId.ToString(CultureInfo.InvariantCulture),
                            member.IsRepresentative.ToString(), isSelected.ToString(), member.Score.ToString("F2", CultureInfo.InvariantCulture),
                            member.BinaryIdentical.ToString(), member.FilePath, member.Width.ToString(CultureInfo.InvariantCulture),
                            member.Height.ToString(CultureInfo.InvariantCulture), member.FileSize.ToString(CultureInfo.InvariantCulture), ""];
                        writer.WriteLine(string.Join(',', cells.Select(SearchReportExporter.EscapeCsvCell)));
                        continue;
                    }
                    if (member.GroupNumber != previous)
                    {
                        if (previous != 0) writer.Write("</section>");
                        previous = member.GroupNumber;
                        writer.Write($"<section><h2>#{previous}</h2>");
                    }
                    writer.Write("<figure>");
                    if (member.ThumbnailPath is string relative)
                    {
                        string thumbnail = Path.GetFullPath(Path.Combine(cacheRoot, relative));
                        if (!ManagedPaths.Contains(cacheRoot, thumbnail)) throw new IOException("Invalid thumbnail path.");
                        ManagedPaths.RejectLinks(thumbnail);
                        if (File.Exists(thumbnail))
                        {
                            string name = (++asset).ToString(CultureInfo.InvariantCulture) + ".jpg";
                            File.Copy(thumbnail, Path.Combine(temporary, "assets", name), false);
                            writer.Write($"<img loading=\"lazy\" alt=\"Preview\" src=\"assets/{name}\">");
                        }
                    }
                    writer.Write("<figcaption><strong>" + HtmlEncoder.Default.Encode(Path.GetFileName(member.FilePath)) + "</strong><small>" +
                        HtmlEncoder.Default.Encode(member.FilePath) + $"</small><small>{member.Width} × {member.Height} · {member.FileSize} B · {member.Score:F2}%</small>" +
                        "<small>" + HtmlEncoder.Default.Encode(member.IsRepresentative ? labels.Representative : member.BinaryIdentical ? labels.BinaryIdentical : labels.CloseVariant)
                        + " · " + HtmlEncoder.Default.Encode(labels.Selected + ": " + (isSelected ? labels.Yes : labels.No)) + "</small></figcaption></figure>");
                }
                if (html) writer.Write((previous != 0 ? "</section>" : "") + "</body></html>");
            }
            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(temporary, GeneratedArtifacts.ReportMarker), GeneratedArtifacts.ReportSignature);
            Directory.Move(temporary, published);
            return Path.Combine(published, file);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
}
