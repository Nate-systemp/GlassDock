using System.Numerics;
using GlassDock.Core.Materials;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using W = global::Windows.UI.Composition;

namespace GlassDock.App.Rendering;

/// <summary>Clear-only lighting. Borrows the final wave path; owns all light/mask resources.</summary>
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

    public void Apply(GlassMaterial material)
    {
        thickness = material.BorderThickness;
        stroke.StrokeThickness = (float)(thickness * 4 * scale);
        for (var i = 0; i < stops.Length; i++)
            stops[i].Color = global::Windows.UI.Color.FromArgb((byte)Math.Round(255 * DockMaterialRendering.SpecularAlpha(i / 16d,
                thickness > 0 ? material.BorderOpacity : 0)), 255, 255, 255);
    }

    public void SetBounds(double width, double height, double top, double dockHeight, double dpi)
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
