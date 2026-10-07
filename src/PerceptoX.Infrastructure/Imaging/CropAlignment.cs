using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PerceptoX.Infrastructure.Imaging;

/// <summary>Experimental translation/scale-only registration. Rejects low-texture and ambiguous matches.</summary>
internal static class CropAlignment
{
    internal static Rectangle? Find(Image<Rgba32> reference, Image<Rgba32> original, CancellationToken token)
    {
        const int samples = 24;
        Rgba32[] query = new Rgba32[samples * samples];
        double sum = 0, squared = 0;
        for (int y = 0; y < samples; y++)
            for (int x = 0; x < samples; x++)
            {
                Rgba32 pixel = reference[Math.Min(reference.Width - 1, (int)((x + .5) * reference.Width / samples)),
                    Math.Min(reference.Height - 1, (int)((y + .5) * reference.Height / samples))];
                query[y * samples + x] = pixel;
                double gray = (pixel.R + pixel.G + pixel.B) / 3d;
                sum += gray; squared += gray * gray;
            }
        double count = query.Length;
        if (Math.Sqrt(Math.Max(0, squared / count - Math.Pow(sum / count, 2))) < 10) return null;
        double aspect = (double)reference.Width / reference.Height;
        int maximumWidth = Math.Min(original.Width, (int)(original.Height * aspect));
        List<(Rectangle Box, double Error)> candidates = [];
        for (int scale = 8; scale <= 40; scale++)
        {
            token.ThrowIfCancellationRequested();
            int width = Math.Max(1, maximumWidth * scale / 40), height = Math.Max(1, (int)(width / aspect));
            int step = Math.Max(1, width / 8);
            for (int y = 0; y <= original.Height - height; y += step)
                for (int x = 0; x <= original.Width - width; x += step)
                {
                    Rectangle box = new(x, y, width, height);
                    candidates.Add((box, Error(original, query, samples, box)));
                }
        }
        var best = candidates.MinBy(candidate => candidate.Error);
        Rectangle coarse = best.Box;
        int delta = Math.Max(1, coarse.Width / 8);
        // Local refinement around the best coarse scale/translation, bounded independently of source MP.
        for (int scale = -2; scale <= 2; scale++)
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    token.ThrowIfCancellationRequested();
                    int width = Math.Max(1, coarse.Width + scale * Math.Max(1, maximumWidth / 80));
                    int height = Math.Max(1, (int)(width / aspect));
                    int x = coarse.X + dx * Math.Max(1, delta / 4), y = coarse.Y + dy * Math.Max(1, delta / 4);
                    if (x < 0 || y < 0 || x + width > original.Width || y + height > original.Height) continue;
                    Rectangle box = new(x, y, width, height);
                    double error = Error(original, query, samples, box);
                    if (error < best.Error) best = (box, error);
                }
        if (best.Error > .065) return null;
        double alternative = candidates.Where(candidate => Overlap(candidate.Box, best.Box) < .5)
            .Select(candidate => candidate.Error).DefaultIfEmpty(1).Min();
        return alternative - best.Error >= .02 ? best.Box : null;
    }

    private static double Error(Image<Rgba32> source, Rgba32[] query, int samples, Rectangle box)
    {
        long error = 0;
        for (int y = 0; y < samples; y++)
            for (int x = 0; x < samples; x++)
            {
                Rgba32 a = query[y * samples + x];
                Rgba32 b = source[Math.Min(source.Width - 1, box.X + (int)((x + .5) * box.Width / samples)),
                    Math.Min(source.Height - 1, box.Y + (int)((y + .5) * box.Height / samples))];
                error += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            }
        return error / (query.Length * 3 * 255d);
    }
    private static double Overlap(Rectangle first, Rectangle second)
    {
        Rectangle intersection = Rectangle.Intersect(first, second);
        double area = (double)intersection.Width * intersection.Height;
        return area / ((double)first.Width * first.Height + (double)second.Width * second.Height - area);
    }
}
