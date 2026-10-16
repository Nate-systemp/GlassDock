using System.Numerics;
using GlassDock.Core.Materials;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;

namespace GlassDock.App.Rendering;

/// <summary>Antialiased optical catches stroked on the actual retained clipping contour.</summary>
internal sealed class LiquidGlassRim : IDisposable
{
    private CanvasLinearGradientBrush? brush;

    public void Draw(CanvasDrawingSession drawing, CanvasGeometry geometry, Vector4 bounds,
        float scale, LiquidGlassMaterial material, bool dockSurface = false, float strokeScale = 1)
    {
        if (brush is null)
        {
            var stops = new CanvasGradientStop[33];
            for (var i = 0; i < stops.Length; i++)
            {
                var t = i / 32f;
                var alpha = Math.Exp(-Math.Pow((t - .10) / .13, 2))
                    + .65 * Math.Exp(-Math.Pow((t - .90) / .13, 2));
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
        if (dockSurface) DrawDockLight(drawing, geometry, bounds, scale);
        // The body's exact vector clip retains the inner half of a 1.4-DIP stroke.
        // Coverage is resolved by Direct2D, not a subpixel exponential sampled once per pixel.
        if (dockSurface) brush.Opacity *= .55f;
        drawing.DrawGeometry(geometry, brush, 1.4f * scale * strokeScale);
    }

    private CanvasLinearGradientBrush? glow;

    // Clear main dock only: soft internal glow without a continuous white border.
    // Both are vertical (shape-following) and drawn inside the existing clip,
    // so the hover wave contour is respected automatically.
    private void DrawDockLight(CanvasDrawingSession drawing, CanvasGeometry geometry, Vector4 bounds, float scale)
    {
        static CanvasGradientStop Stop(float at, byte alpha) =>
            new() { Position = at, Color = global::Windows.UI.Color.FromArgb(alpha, 255, 255, 255) };
        glow ??= new(drawing.Device, [Stop(0, 34), Stop(.18f, 16), Stop(.5f, 3), Stop(.82f, 0), Stop(1, 12)]);
        // Wave rise extends above Dock.Y; start the gradient a little higher so
        // the raised crest is lit like the resting top edge.
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
    }
}
