using PerceptoX.Presentation.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PerceptoX.Infrastructure.Imaging;
using PerceptoX.Presentation.Services;
using Windows.Foundation;

namespace PerceptoX.WinUI.Services;

/// <summary>Reuses two views of one bitmap. Hover changes only layout; it never decodes a file.</summary>
internal sealed class ComparisonImagePane : IDisposable
{
    private const double Diameter = 128;
    private readonly Grid _surface;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Image _magnified = new() { Stretch = Stretch.Fill };
    private readonly Border _lens;
    private int _width, _height;

    internal ComparisonImagePane(string heading, ComparisonImageMetadata metadata, BitmapImage bitmap,
        int width, int height, double paneWidth, double paneHeight)
    {
        _surface = new() { Width = Math.Max(1, paneWidth - 2), Height = paneHeight, Background = ThemeManager.Brush("PxImageSurface") };
        Canvas lensContent = new() { Width = Diameter, Height = Diameter,
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, Diameter, Diameter) } };
        lensContent.Children.Add(_magnified);
        lensContent.Children.Add(new Border { Width = Diameter, Height = Diameter,
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue), BorderThickness = new Thickness(2),
            IsHitTestVisible = false });
        _lens = new() { Width = Diameter, Height = Diameter, Background = ThemeManager.Brush("PxImageSurface"),
            Child = lensContent, Visibility = Visibility.Collapsed };
        Canvas overlay = new() { IsHitTestVisible = false };
        overlay.Children.Add(_lens);
        _surface.Children.Add(_image); _surface.Children.Add(overlay);
        Viewer = new() { ZoomMode = ZoomMode.Enabled, MinZoomFactor = 1, MaxZoomFactor = 8,
            HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _surface, Height = paneHeight + 2 };
        View = new() { Spacing = 8 };
        View.Children.Add(new TextBlock { Text = heading, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        View.Children.Add(new Border { CornerRadius = new CornerRadius(12), BorderBrush = ThemeManager.Brush("PxLine"),
            BorderThickness = new Thickness(1), Child = Viewer });
        View.Children.Add(DialogLayout.Section(UiText.T("ComparisonImagePane.Text001"),
            new TextBlock { Text = metadata.FileName, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            new TextBlock { Text = metadata.FilePath, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            DialogLayout.Text(UiText.T("ComparisonImagePane.Text002", metadata.Width, metadata.Height, FormatSize(metadata.FileSize)))));
        SetSource(bitmap, width, height);
        _surface.PointerMoved += PointerMoved;
        _surface.PointerExited += PointerExited;
    }

    internal StackPanel View { get; }
    internal ScrollViewer Viewer { get; }
    internal event Action<RelativeImagePoint?>? HoverChanged;

    internal void SetSource(BitmapImage bitmap, int width, int height)
    {
        _image.Source = bitmap; _magnified.Source = bitmap;
        _width = width; _height = height;
    }

    private FittedImage Fit => ComparisonLoupeGeometry.Fit(_surface.Width, _surface.Height, _width, _height);

    private void PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        Point position = args.GetCurrentPoint(_surface).Position;
        HoverChanged?.Invoke(ComparisonLoupeGeometry.Position(Fit, position.X, position.Y));
    }

    private void PointerExited(object sender, PointerRoutedEventArgs args) => HoverChanged?.Invoke(null);

    internal void ShowLoupe(RelativeImagePoint point, double zoom)
    {
        LoupePlacement placement = ComparisonLoupeGeometry.Place(Fit, point, zoom, Diameter, _surface.Width, _surface.Height);
        Canvas.SetLeft(_lens, placement.Left); Canvas.SetTop(_lens, placement.Top);
        _magnified.Width = placement.ImageWidth; _magnified.Height = placement.ImageHeight;
        Canvas.SetLeft(_magnified, placement.ImageLeft); Canvas.SetTop(_magnified, placement.ImageTop);
        _lens.Visibility = Visibility.Visible;
    }

    internal void HideLoupe() => _lens.Visibility = Visibility.Collapsed;

    private static string FormatSize(long bytes) => bytes >= 1_048_576 ? UiText.T("ComparisonImagePane.Text003", bytes / 1_048_576d) : UiText.T("ComparisonImagePane.Text004", bytes / 1024d);

    public void Dispose()
    {
        _surface.PointerMoved -= PointerMoved; _surface.PointerExited -= PointerExited;
        HoverChanged = null;
        _image.Source = null; _magnified.Source = null;
        Viewer.Content = null;
    }
}
