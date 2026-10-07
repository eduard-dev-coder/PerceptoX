using System.Diagnostics;
using PerceptoX.Application.Imaging;
using PerceptoX.Application.Matching;
using PerceptoX.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

if (args.Length is 2 or 3 && args[0] == "--verify" &&
    (args.Length == 2 || args[2] == "--with-thumbnails"))
{
    return VerifyDataset(args[1], args.Length == 3);
}

if (args.Length is not (2 or 4) || args[0] != "--output" ||
    (args.Length == 4 && args[2] != "--base-count"))
{
    Console.Error.WriteLine("Usage: PerceptoX.Dataset --output <new-directory> [--base-count <1..1000>] | --verify <complete-dataset-directory> [--with-thumbnails]");
    return 2;
}

int baseCount = 1000;
if (args.Length == 4 && (!int.TryParse(args[3], out baseCount) || baseCount is < 1 or > 1000))
{
    Console.Error.WriteLine("--base-count must be between 1 and 1000.");
    return 2;
}

string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) || File.Exists(output))
{
    Console.Error.WriteLine("Output must be a new path; existing data is never overwritten.");
    return 2;
}

Directory.CreateDirectory(output);
using StreamWriter manifest = new(Path.Combine(output, "manifest.csv"));
manifest.WriteLine("relative_path,base_id,transform");

for (int id = 0; id < baseCount; id++)
{
    string folderName = $"{id:D4}";
    string folder = Path.Combine(output, "images", folderName);
    Directory.CreateDirectory(folder);

    using Image<Rgba32> source = CreateSource(id);
    SavePng("original", source);

    using (Image<Rgba32> image = source.Clone(context => context.Resize(96, 64)))
    {
        SaveJpeg("thumbnail", image, 85);
    }

    using (Image<Rgba32> image = source.Clone(context => context.Resize(288, 192)))
    {
        SaveJpeg("resized-large", image, 90);
    }

    SaveJpeg("compressed-40", source, 40);
    SaveJpeg("compressed-75", source, 75);

    using (Image<Rgba32> image = source.Clone(context => context.Crop(new Rectangle(24, 16, 144, 96))))
    {
        SaveJpeg("crop-center", image, 85);
    }

    using (Image<Rgba32> image = source.Clone(context => context.Crop(new Rectangle(0, 0, 144, 96))))
    {
        SaveJpeg("crop-corner", image, 85);
    }

    using (Image<Rgba32> image = source.Clone())
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 86; y < 102; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 12; x < 180; x++)
                {
                    if ((x / 7 + y / 3) % 5 < 3)
                    {
                        row[x] = new Rgba32(245, 245, 245);
                    }
                }
            }
        });
        SaveJpeg("overlay", image, 85);
    }

    using (Image<Rgba32> image = source.Clone(context => context.Resize(152, 101)))
    {
        SaveJpeg("resized-small", image, 85);
    }

    using (Image<Rgba32> screenshot = new(224, 160, new Rgba32(34, 39, 46)))
    {
        Rgba32[] sourcePixels = new Rgba32[source.Width * source.Height];
        source.CopyPixelDataTo(sourcePixels);
        screenshot.ProcessPixelRows(targetRows =>
        {
            for (int y = 0; y < source.Height; y++)
            {
                sourcePixels.AsSpan(y * source.Width, source.Width).CopyTo(targetRows.GetRowSpan(y + 16)[16..]);
            }
        });
        SaveJpeg("screenshot-frame", screenshot, 85);
    }

    if ((id + 1) % 100 == 0)
    {
        Console.WriteLine($"Generated {(id + 1) * 10} / {baseCount * 10} images.");
    }

    void SavePng(string transform, Image<Rgba32> image)
    {
        string fileName = $"{folderName}-{transform}.png";
        image.SaveAsPng(Path.Combine(folder, fileName));
        manifest.WriteLine($"images/{folderName}/{fileName},{folderName},{transform}");
    }

    void SaveJpeg(string transform, Image<Rgba32> image, int quality)
    {
        string fileName = $"{folderName}-{transform}.jpg";
        image.SaveAsJpeg(Path.Combine(folder, fileName), new JpegEncoder { Quality = quality });
        manifest.WriteLine($"images/{folderName}/{fileName},{folderName},{transform}");
    }
}

manifest.Flush();
File.WriteAllText(Path.Combine(output, "COMPLETE.txt"), $"base_count={baseCount}\nimage_count={baseCount * 10}\n");
Console.WriteLine($"Complete: {baseCount * 10} generated images in {output}");
return 0;

static Image<Rgba32> CreateSource(int id)
{
    Image<Rgba32> image = new(192, 128);
    image.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < accessor.Height; y++)
        {
            Span<Rgba32> row = accessor.GetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                int tile = ((x / (8 + id % 9)) + (y / (9 + id % 7))) & 1;
                row[x] = new Rgba32(
                    (byte)((x * 3 + y + id * 17 + tile * 40) & 255),
                    (byte)((x + y * 5 + id * 29) & 255),
                    (byte)((x * 7 + y * 2 + id * 11 + tile * 30) & 255));
            }
        }
    });
    return image;
}

static int VerifyDataset(string directory, bool withThumbnails)
{
    string root = Path.GetFullPath(directory);
    string markerPath = Path.Combine(root, "COMPLETE.txt");
    string manifestPath = Path.Combine(root, "manifest.csv");
    if (!File.Exists(markerPath) || !File.Exists(manifestPath))
    {
        Console.Error.WriteLine("The dataset is incomplete or has no manifest.");
        return 2;
    }

    string? countLine = File.ReadLines(markerPath).FirstOrDefault(line => line.StartsWith("image_count=", StringComparison.Ordinal));
    if (countLine is null || !int.TryParse(countLine["image_count=".Length..], out int expectedCount))
    {
        Console.Error.WriteLine("The completion marker has no valid image count.");
        return 2;
    }

    ImageSharpFingerprintExtractor extractor = new();
    using ImageSharpImageProcessor processor = new();
    string thumbnailRoot = Path.Combine(root, ".cache", "thumbs");
    Stopwatch timer = Stopwatch.StartNew();
    int count = 0;
    int failures = 0;
    ulong checksum = 0;
    foreach (string row in File.ReadLines(manifestPath).Skip(1))
    {
        string relativePath = row.Split(',', 2)[0];
        string path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Manifest path escapes dataset root.");
            return 2;
        }

        count++;
        try
        {
            ImageFingerprintSet result;
            if (withThumbnails)
            {
                FileInfo source = new(path);
                string sourceVersion = $"{source.Length}-{source.LastWriteTimeUtc.Ticks}";
                ImageProcessingRequest request = new(path, count, sourceVersion, thumbnailRoot);
                ImageProcessingResult processed = processor.ProcessAsync(request).GetAwaiter().GetResult();
                string thumbnailPath = Path.Combine(
                    thumbnailRoot,
                    processed.Thumbnail.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(thumbnailPath) || processed.Thumbnail.Width > 256 || processed.Thumbnail.Height > 256)
                {
                    throw new InvalidDataException("Thumbnail was not published with bounded dimensions.");
                }

                result = processed.Fingerprints;
            }
            else
            {
                result = extractor.Extract(path);
            }

            if (!result.GetRequired("phash").Value.TryGetUInt64(out ulong value))
            {
                throw new InvalidDataException("Expected a 64-bit pHash.");
            }

            checksum ^= value;
        }
        catch (Exception exception)
        {
            failures++;
            if (failures <= 5)
            {
                Console.Error.WriteLine($"Image {count}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    timer.Stop();
    Console.WriteLine($"Verified {count}/{expectedCount} images; thumbnails={withThumbnails}; failures={failures}; elapsed={timer.Elapsed.TotalSeconds:F2}s; rate={count / timer.Elapsed.TotalSeconds:F1}/s; checksum={checksum:X16}");
    return count == expectedCount && failures == 0 ? 0 : 1;
}
