using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace GlassDock.App.Rendering;

/// <summary>Transparent XAML child output; inherits its popup's existing animation.</summary>
internal sealed class LiquidGlassDrawingTarget : IDisposable
{
    public Grid Element { get; } = new() { IsHitTestVisible = false,
        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top };
    private CompositionGraphicsDevice? graphics;
    private CompositionDrawingSurface? surface;
    private CompositionSurfaceBrush? brush;
    private SpriteVisual? visual;
    private Vector2 size;

    public void Draw(CanvasDevice device, ICanvasImage image, CanvasGeometry geometry,
        global::Windows.Foundation.Rect source, float scale, Action<CanvasDrawingSession, CanvasGeometry> drawRim)
    {
        var next = new Vector2((float)source.Width, (float)source.Height);
        if (surface is null)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(Element).Compositor;
            graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, device);
            surface = graphics.CreateDrawingSurface(new(next.X, next.Y),
                Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
            brush = compositor.CreateSurfaceBrush(surface);
            brush.Stretch = CompositionStretch.Fill;
            visual = compositor.CreateSpriteVisual();
            visual.Brush = brush;
            ElementCompositionPreview.SetElementChildVisual(Element, visual);
            size = next;
        }
        if (size != next) { CanvasComposition.Resize(surface, new(next.X, next.Y)); size = next; }
        Element.Width = next.X / scale; Element.Height = next.Y / scale;
        visual!.Size = next / scale;
        using var drawing = CanvasComposition.CreateDrawingSession(surface);
        drawing.Clear(Microsoft.UI.Colors.Transparent);
        using (drawing.CreateLayer(1, geometry))
        {
            drawing.DrawImage(image, Vector2.Zero, source);
            drawRim(drawing, geometry);
        }
    }

    public void Dispose()
    {
        ElementCompositionPreview.SetElementChildVisual(Element, null);
        if (visual is not null) visual.Brush = null;
        if (brush is not null) brush.Surface = null;
        visual?.Dispose(); visual = null;
        brush?.Dispose(); brush = null;
        surface?.Dispose(); surface = null;
        graphics?.Dispose(); graphics = null;
    }
}
