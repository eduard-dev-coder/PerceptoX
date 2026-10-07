using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace PerceptoX.WinUI.Services;

/// <summary>Small GPU surface, reused across messages; no per-frame CPU rasterization.</summary>
internal sealed class TitlebarMessageBlur : IDisposable
{
    private readonly FrameworkElement _host;
    private readonly CompositionVisualSurface _surface;
    private readonly CompositionSurfaceBrush _surfaceBrush;
    private readonly CompositionEffectFactory _factory;
    private readonly CompositionEffectBrush _brush;
    private readonly SpriteVisual _visual;
    private bool _disposed;

    internal TitlebarMessageBlur(FrameworkElement source, FrameworkElement host)
    {
        _host = host;
        Visual sourceVisual = ElementCompositionPreview.GetElementVisual(source);
        Compositor compositor = sourceVisual.Compositor;
        _surface = compositor.CreateVisualSurface();
        _surface.SourceVisual = sourceVisual;
        _surfaceBrush = compositor.CreateSurfaceBrush(_surface);
        using GaussianBlurEffect effect = new() { BlurAmount = 1.35f, BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Speed, Source = new CompositionEffectSourceParameter("text") };
        _factory = compositor.CreateEffectFactory(effect);
        _brush = _factory.CreateBrush(); _brush.SetSourceParameter("text", _surfaceBrush);
        _visual = compositor.CreateSpriteVisual(); _visual.Brush = _brush; _visual.Opacity = 0;
        Resize(source.ActualWidth);
        ElementCompositionPreview.SetElementChildVisual(host, _visual);
    }

    internal void Resize(double width)
    {
        _surface.SourceSize = new Vector2((float)Math.Max(1, width), 48);
        _visual.Size = _surface.SourceSize;
    }

    internal void Begin(TimeSpan duration)
    {
        Compositor compositor = _visual.Compositor;
        using ScalarKeyFrameAnimation opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = duration; opacity.InsertKeyFrame(0, 0); opacity.InsertKeyFrame(0.45f, 0.22f); opacity.InsertKeyFrame(1, 0);
        _visual.StartAnimation("Opacity", opacity);
        using Vector3KeyFrameAnimation movement = compositor.CreateVector3KeyFrameAnimation();
        movement.Duration = duration; movement.InsertKeyFrame(0, Vector3.Zero); movement.InsertKeyFrame(1, new Vector3(0, -24, 0));
        _visual.StartAnimation("Offset", movement);
    }

    internal void Stop()
    {
        _visual.StopAnimation("Opacity"); _visual.StopAnimation("Offset"); _visual.Opacity = 0; _visual.Offset = Vector3.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _visual.Dispose(); _brush.Dispose(); _factory.Dispose(); _surfaceBrush.Dispose(); _surface.Dispose();
    }
}
