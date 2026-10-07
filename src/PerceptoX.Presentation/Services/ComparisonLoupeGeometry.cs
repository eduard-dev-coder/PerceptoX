namespace PerceptoX.Presentation.Services;

public readonly record struct RelativeImagePoint(double X, double Y);
public readonly record struct FittedImage(double Left, double Top, double Width, double Height);
public readonly record struct LoupePlacement(double Left, double Top, double ImageLeft, double ImageTop,
    double ImageWidth, double ImageHeight);

/// <summary>Pure geometry: coordinates exclude Uniform-stretch letterboxing; no image decoding.</summary>
public static class ComparisonLoupeGeometry
{
    public static FittedImage Fit(double boxWidth, double boxHeight, double imageWidth, double imageHeight)
    {
        if (!Valid(boxWidth) || !Valid(boxHeight) || !Valid(imageWidth) || !Valid(imageHeight)) return default;
        double scale = Math.Min(boxWidth / imageWidth, boxHeight / imageHeight);
        double width = imageWidth * scale, height = imageHeight * scale;
        return new((boxWidth - width) / 2, (boxHeight - height) / 2, width, height);
    }

    public static RelativeImagePoint? Position(FittedImage image, double x, double y)
    {
        if (!Valid(image.Width) || !Valid(image.Height) || !double.IsFinite(x) || !double.IsFinite(y) ||
            x < image.Left || y < image.Top || x > image.Left + image.Width || y > image.Top + image.Height) return null;
        return new(Math.Clamp((x - image.Left) / image.Width, 0, 1), Math.Clamp((y - image.Top) / image.Height, 0, 1));
    }

    public static LoupePlacement Place(FittedImage image, RelativeImagePoint point, double zoom,
        double diameter, double boxWidth, double boxHeight)
    {
        if (zoom is not (2 or 4)) throw new ArgumentOutOfRangeException(nameof(zoom));
        if (!Valid(diameter) || !Valid(boxWidth) || !Valid(boxHeight) || !Valid(image.Width) || !Valid(image.Height) ||
            !double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
        double x = Math.Clamp(point.X, 0, 1), y = Math.Clamp(point.Y, 0, 1);
        return new(
            Math.Clamp(image.Left + x * image.Width - diameter / 2, 0, Math.Max(0, boxWidth - diameter)),
            Math.Clamp(image.Top + y * image.Height - diameter / 2, 0, Math.Max(0, boxHeight - diameter)),
            diameter / 2 - x * image.Width * zoom, diameter / 2 - y * image.Height * zoom,
            image.Width * zoom, image.Height * zoom);
    }

    private static bool Valid(double value) => double.IsFinite(value) && value > 0;
}
