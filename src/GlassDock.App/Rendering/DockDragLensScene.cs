using System.Numerics;
using GlassDock.App.Controls;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace GlassDock.App.Rendering;

/// <summary>GPU sampler of the existing dock artwork, indicators and badge labels.
/// Uploads native icon pixels once per gesture; moving icons only change draw coordinates.</summary>
internal sealed class DockDragLensScene : IDisposable
{
    private sealed class Artwork(CanvasBitmap? bitmap, FrameworkElement element, Border tile)
    {
        public CanvasBitmap? Bitmap = bitmap;
        public FrameworkElement Element = element;
        public Border Tile = tile;
        public Rect Bounds, TileBounds;
    }
    private sealed class Item(Button button, AdaptiveAppIcon icon, Artwork[] art, Border? indicator)
    {
        public Button Button = button;
        public AdaptiveAppIcon Icon = icon;
        public Artwork[] Art = art;
        public Border? Indicator = indicator;
        public Rect IndicatorBounds, BadgeBounds;
        public NotificationBadge? Badge;
        public string? Label;
        public CanvasTextLayout? Text;
    }
    private readonly List<Item> items = [];
    private readonly CanvasTextFormat text = new()
    {
        FontFamily = "Segoe UI", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        HorizontalAlignment = CanvasHorizontalAlignment.Center, VerticalAlignment = CanvasVerticalAlignment.Center
    };
    private readonly float scale;

    public DockDragLensScene(IEnumerable<Button> buttons, Button dragged, FrameworkElement root, float scale)
    {
        this.scale = scale;
        var device = CanvasDevice.GetSharedDevice();
        try
        {
            foreach (var button in buttons)
            {
                if (ReferenceEquals(button, dragged) || button.Content is not Grid content ||
                    content.Children[0] is not AdaptiveAppIcon icon) continue;
                var art = new List<Artwork>();
                try { icon.VisitLensArtwork((source, element, tile) =>
                {
                    var bitmap = source is null ? null : CanvasBitmap.CreateFromBytes(device, source.Pixels, source.Width, source.Height,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
                    art.Add(new(bitmap, element, tile));
                }); }
                catch { foreach (var a in art) a.Bitmap?.Dispose(); throw; }
                var indicator = content.Children.Count > 1 ? content.Children[1] as Border : null;
                items.Add(new(button, icon, art.ToArray(), indicator));
            }
            Rebase(root);
        }
        catch { Dispose(); throw; }
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement root) =>
        element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    public void Rebase(FrameworkElement root)
    {
        foreach (var item in items)
        {
            var t = item.Button.RenderTransform as TranslateTransform;
            Rect Base(FrameworkElement element)
            { var r = Bounds(element, root); r.X -= t?.X ?? 0; r.Y -= t?.Y ?? 0; return r; }
            foreach (var art in item.Art)
            { art.Bounds = Base(art.Element); art.TileBounds = Base(art.Tile); }
            item.Badge = item.Icon.LensBadge;
            if (item.Indicator is not null) item.IndicatorBounds = Base(item.Indicator);
            if (item.Badge is not null) item.BadgeBounds = Base(item.Badge.LensBody);
        }
    }

    public void Draw(CanvasDrawingSession drawing)
    {
        drawing.Transform = Matrix3x2.CreateScale(scale);
        foreach (var item in items)
        {
            var translation = item.Button.RenderTransform as TranslateTransform;
            var dx = translation?.X ?? 0; var dy = translation?.Y ?? 0;
            foreach (var art in item.Art)
            {
                // Keep each card immediately behind its own artwork, in the
                // same back-to-front order as XAML (not all cards, then icons).
                if (art.Tile is { Visibility: Visibility.Visible, Background: SolidColorBrush tileBrush } tile)
                {
                    var card = art.TileBounds; card.X += dx; card.Y += dy;
                    var radius = (float)tile.CornerRadius.TopLeft;
                    var color = tileBrush.Color; color.A = (byte)(color.A * tile.Opacity);
                    drawing.FillRoundedRectangle(card, radius, radius, color);
                    if (tile.BorderBrush is SolidColorBrush border && tile.BorderThickness.Left > 0)
                        drawing.DrawRoundedRectangle(card, radius, radius, border.Color, (float)tile.BorderThickness.Left);
                }
                var r = art.Bounds; r.X += dx; r.Y += dy;
                if (art.Bitmap is not null) drawing.DrawImage(art.Bitmap, r, art.Bitmap.Bounds);
            }
            if (item.Indicator is { Visibility: Visibility.Visible } indicator &&
                indicator.Background is SolidColorBrush brush)
            {
                var r = item.IndicatorBounds; r.X += dx; r.Y += dy;
                var color = brush.Color; color.A = (byte)(color.A * indicator.Opacity);
                drawing.FillRoundedRectangle(r, 1.5f, 1.5f, color);
            }
            if (item.Badge is { Visibility: Visibility.Visible } badge)
            {
                var r = item.BadgeBounds; r.X += dx; r.Y += dy;
                drawing.FillEllipse((float)(r.X + r.Width / 2), (float)(r.Y + r.Height / 2),
                    (float)r.Width / 2, (float)r.Height / 2, Color.FromArgb(255, 207, 38, 55));
                if (item.Label != badge.LensText)
                {
                    item.Text?.Dispose(); item.Label = badge.LensText;
                    item.Text = new CanvasTextLayout(drawing.Device, item.Label, text, (float)r.Width, (float)r.Height);
                }
                if (item.Text is not null) drawing.DrawTextLayout(item.Text, new Vector2((float)r.X, (float)r.Y), Microsoft.UI.Colors.White);
            }
        }
        drawing.Transform = Matrix3x2.Identity;
    }

    public void Dispose()
    {
        foreach (var item in items)
        { foreach (var art in item.Art) art.Bitmap?.Dispose(); item.Text?.Dispose(); }
        items.Clear(); text.Dispose();
    }
}
