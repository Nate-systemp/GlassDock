using System.Numerics;
using GlassDock.Core.Materials;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;

namespace GlassDock.App.Rendering;

internal sealed class LiquidGlassRim : IDisposable
{
    private CanvasLinearGradientBrush? brush;
    private LiquidGlassMaterial? applied;

    public void Draw(CanvasDrawingSession drawing, CanvasGeometry geometry, Vector4 bounds,
        float scale, LiquidGlassMaterial material, bool dockSurface = false, float strokeScale = 1)
    {
        if (brush is null || applied != material)
        {
            brush?.Dispose();
            glow?.Dispose(); glow = null;
            applied = material;
            var stops = new CanvasGradientStop[33];
            for (var i = 0; i < stops.Length; i++)
            {
                var t = i / 32f;
                var alpha = Math.Exp(-Math.Pow((t - material.HighlightPosition) / material.HighlightSoftness, 2))
                    + .65 * Math.Exp(-Math.Pow((t - Math.Min(1, material.HighlightPosition + material.HighlightSpread)) / material.HighlightSoftness, 2));
                alpha = Math.Pow(alpha, material.SpecularFalloff / 2.5) *
                    (material.TopLight * (1 - t) + material.BottomLight * t);
                stops[i] = new() { Position = t, Color = global::Windows.UI.Color.FromArgb(
                    (byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), 240, 247, 255) };
            }
            brush = new(drawing.Device, stops);
        }
        var gradient = DockSpecularLighting.Gradient(bounds.Z, bounds.W, material.SpecularAngleDegrees);
        var origin = new Vector2(bounds.X, bounds.Y);
        brush.StartPoint = (origin + gradient.Start) * scale;
        brush.EndPoint = (origin + gradient.End) * scale;
        brush.Opacity = Math.Clamp(material.SpecularIntensity * 2.4f, 0, 1);
        drawing.Antialiasing = CanvasAntialiasing.Antialiased;
        DrawDockLight(drawing, geometry, bounds, scale, material);
        if (dockSurface) brush.Opacity *= .55f;
        brush.Opacity *= material.SurfaceOpacity;
        if (material.InnerGlint > 0)
        {
            var opacity = brush.Opacity;
            brush.Opacity *= material.InnerGlint * .25f;
            drawing.DrawGeometry(geometry, brush, material.HighlightWidth * 3 * scale * strokeScale);
            brush.Opacity = opacity;
        }
        brush.Opacity *= material.OuterGlint;
        drawing.DrawGeometry(geometry, brush, material.HighlightWidth * scale * strokeScale);
        if (material.EdgeOpacity > 0 && material.EdgeWidth > 0)
            drawing.DrawGeometry(geometry, global::Windows.UI.Color.FromArgb(
                (byte)(255 * material.EdgeOpacity * material.SurfaceOpacity), 255, 255, 255),
                material.EdgeWidth * scale * strokeScale);
    }

    private CanvasLinearGradientBrush? glow;

    private void DrawDockLight(CanvasDrawingSession drawing, CanvasGeometry geometry, Vector4 bounds, float scale, LiquidGlassMaterial material)
    {
        static CanvasGradientStop Stop(float at, byte alpha) =>
            new() { Position = at, Color = global::Windows.UI.Color.FromArgb(alpha, 255, 255, 255) };
        byte Alpha(float value) => (byte)Math.Clamp(value * material.InnerShine * material.SpecularIntensity / .16f * material.SurfaceOpacity, 0, 255);
        glow ??= new(drawing.Device, new[] { Stop(0, Alpha(34 * material.TopLight)), Stop(.18f, Alpha(16 * material.TopLight)),
            Stop(.5f, Alpha(3)), Stop(.82f, 0), Stop(1, Alpha(12 * material.BottomLight)) });
        var top = new Vector2(0, (bounds.Y - bounds.W * .25f) * scale);
        var bottom = new Vector2(0, (bounds.Y + bounds.W) * scale);
        glow.StartPoint = top;
        glow.EndPoint = bottom;
        drawing.FillGeometry(geometry, glow);
    }

    public void Dispose()
    {
        brush?.Dispose(); brush = null;
        glow?.Dispose(); glow = null;
        applied = null;
    }
}
