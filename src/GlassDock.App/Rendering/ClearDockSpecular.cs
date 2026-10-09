using System.Numerics;
using GlassDock.Core.Materials;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using W = global::Windows.UI.Composition;

namespace GlassDock.App.Rendering;

/// <summary>Rim lighting. Borrows the final wave path; owns all light/mask resources.</summary>
internal sealed class ClearDockSpecular : IDisposable
{
    private readonly W.CompositionSpriteShape stroke;
    private readonly W.CompositionColorBrush white;
    private readonly W.ShapeVisual visual;
    private readonly W.ContainerVisual capture;
    private readonly W.CompositionVisualSurface surface;
    private readonly W.CompositionSurfaceBrush mask;
    private readonly W.CompositionLinearGradientBrush light;
    private readonly W.CompositionColorGradientStop[] stops;
    private readonly W.CompositionMaskBrush lighting;
    private readonly W.SpriteVisual lightVisual;
    private readonly W.CompositionVisualSurface lightSurface;
    private readonly W.CompositionSurfaceBrush lightSource;
    private readonly W.CompositionEffectFactory factory;
    public W.CompositionEffectBrush Brush { get; }
    private double thickness, scale = 1;
    private bool diagonal;

    public ClearDockSpecular(W.Compositor compositor, W.CompositionPathGeometry geometry, W.CompositionBrush body)
    {
        white = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));
        stroke = compositor.CreateSpriteShape(geometry);
        stroke.StrokeBrush = white;
        stroke.StrokeLineJoin = W.CompositionStrokeLineJoin.Round;
        stroke.Scale = new Vector2(2);
        visual = compositor.CreateShapeVisual(); visual.BorderMode = W.CompositionBorderMode.Soft;
        visual.Shapes.Add(stroke);
        capture = compositor.CreateContainerVisual(); capture.Children.InsertAtTop(visual);
        surface = compositor.CreateVisualSurface(); surface.SourceVisual = capture;
        mask = compositor.CreateSurfaceBrush(surface); mask.Stretch = W.CompositionStretch.Fill;
        mask.BitmapInterpolationMode = W.CompositionBitmapInterpolationMode.Linear;
        light = compositor.CreateLinearGradientBrush(); light.MappingMode = W.CompositionMappingMode.Absolute;
        stops = new W.CompositionColorGradientStop[17];
        for (var i = 0; i < stops.Length; i++)
        {
            stops[i] = compositor.CreateColorGradientStop(i / 16f, global::Windows.UI.Color.FromArgb(0, 255, 255, 255));
            light.ColorStops.Add(stops[i]);
        }
        lighting = compositor.CreateMaskBrush(); lighting.Source = light; lighting.Mask = mask;
        // A mask brush is not a supported effect input. Capture its pixels first.
        lightVisual = compositor.CreateSpriteVisual(); lightVisual.Brush = lighting;
        lightSurface = compositor.CreateVisualSurface(); lightSurface.SourceVisual = lightVisual;
        lightSource = compositor.CreateSurfaceBrush(lightSurface);
        lightSource.Stretch = W.CompositionStretch.Fill;
        factory = compositor.CreateEffectFactory(new CompositeEffect
        {
            Mode = CanvasComposite.SourceOver,
            Sources = { new W.CompositionEffectSourceParameter("Body"), new W.CompositionEffectSourceParameter("Light") }
        });
        Brush = factory.CreateBrush(); Brush.SetSourceParameter("Body", body); Brush.SetSourceParameter("Light", lightSource);
    }

    public void Apply(GlassMaterial material, bool diagonalCatches = false)
    {
        diagonal = diagonalCatches;
        thickness = material.BorderThickness;
        stroke.StrokeThickness = (float)(thickness * 4 * scale);
        for (var i = 0; i < stops.Length; i++)
        {
            var t = i / 16d;
            // Short, smoothly faded opposing catches; the middle stays unlit.
            var alpha = diagonal
                ? material.BorderOpacity * (Math.Exp(-Math.Pow((t - .10) / .13, 2))
                    + .65 * Math.Exp(-Math.Pow((t - .90) / .13, 2)))
                : DockMaterialRendering.SpecularAlpha(t, material.BorderOpacity);
            stops[i].Color = global::Windows.UI.Color.FromArgb(
                (byte)Math.Round(255 * Math.Clamp(thickness > 0 ? alpha : 0, 0, 1)), 255, 255, 255);
        }
    }

    public void SetBounds(double width, double height, double top, double dockHeight, double dpi, double dockWidth, double angle = 45)
    {
        scale = dpi;
        stroke.StrokeThickness = (float)(thickness * 4 * dpi);
        var size = new Vector2((float)(width * dpi * 2), (float)(height * dpi * 2));
        visual.Size = capture.Size = surface.SourceSize = size;
        lightVisual.Size = lightSurface.SourceSize = size / 2;
        // Only vector coverage is supersampled. Light is evaluated AFTER mask
        // downsampling, in final window pixels, not in the stroke's scaled space.
        light.StartPoint = new(0, (float)(top * dpi));
        light.EndPoint = new(0, (float)((top + dockHeight) * dpi));
        if (diagonal)
        {
            // Equal influence from normalized X/Y despite the dock's wide aspect
            // ratio: the gradient must not turn into a left-to-right stripe.
            var w = Math.Max(1, dockWidth * dpi);
            var h = Math.Max(1, dockHeight * dpi);
            var gradient = DockSpecularLighting.Gradient(w, h, angle);
            var origin = new Vector2((float)((width - dockWidth) * dpi / 2), (float)(top * dpi));
            light.StartPoint = origin + gradient.Start;
            light.EndPoint = origin + gradient.End;
        }
    }

    public void SetTransform(Matrix4x4 matrix) => visual.TransformMatrix = matrix;
    public void SetVisible(bool visible) => visual.IsVisible = visible;

    public void Dispose()
    {
        Brush.Dispose(); factory.Dispose(); lightSource.Dispose(); lightSurface.Dispose(); lightVisual.Dispose(); lighting.Dispose();
        foreach (var stop in stops) stop.Dispose();
        light.Dispose(); mask.Dispose(); surface.Dispose(); capture.Dispose(); visual.Dispose(); stroke.Dispose(); white.Dispose();
    }
}
