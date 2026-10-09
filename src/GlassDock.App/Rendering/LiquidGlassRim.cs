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
        float scale, LiquidGlassMaterial material)
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
        // The body's exact vector clip retains the inner half of a 1.4-DIP stroke.
        // Coverage is resolved by Direct2D, not a subpixel exponential sampled once per pixel.
        drawing.DrawGeometry(geometry, brush, 1.4f * scale);
    }

    public void Dispose() { brush?.Dispose(); brush = null; }
}
