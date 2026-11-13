using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace GlassDock.App.Controls;

internal sealed class QuickSettingsGridButton : Button, IDisposable
{
    public UtilityGlassSurface Glass { get; } = new();
    private readonly Canvas cells = new() { Width = 18, Height = 18, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Rectangle square = new() { Width = 18, Height = 18, RadiusX = 3, RadiusY = 3,
        StrokeThickness = 1.6, Opacity = 0, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border fallback = new() { CornerRadius = new(18), IsHitTestVisible = false };
    private readonly Rectangle expandingCell;
    private Storyboard? motion;
    private bool? openTarget;

    internal QuickSettingsGridButton()
    {
        Width = Height = 36; MinWidth = MinHeight = 0; Padding = new(0); BorderThickness = new(0); CornerRadius = new(18);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var face = new Grid { Width = 36, Height = 36 }; face.Children.Add(fallback); face.Children.Add(Glass.Element);
        face.Children.Add(cells); face.Children.Add(square); Content = face;
        for (var i = 0; i < 4; i++)
        {
            var cell = new Rectangle { Width = 7, Height = 7, RadiusX = 1.8, RadiusY = 1.8, StrokeThickness = 1.6 };
            Canvas.SetLeft(cell, i % 2 * 11); Canvas.SetTop(cell, i / 2 * 11); cells.Children.Add(cell);
        }
        expandingCell = (Rectangle)cells.Children[0];
        AutomationProperties.SetName(this, "Open Quick Settings");
    }

    internal void Configure(DockAppearanceMode mode, UtilityTheme theme, GlassMaterial material, LiquidGlassMaterial optics, bool highlights)
    {
        var iconTheme = mode.GlassStyle() is null ? mode == DockAppearanceMode.Light ? UtilityTheme.Light : UtilityTheme.Dark : theme;
        var color = UtilityColors.Icon(iconTheme, false);
        var brush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color));
        square.Stroke = brush;
        foreach (var cell in cells.Children.OfType<Rectangle>()) cell.Stroke = brush;
        fallback.Background = new SolidColorBrush(mode.GlassStyle() is null
            ? Desktop.DockControlPalette.SolidSurface(mode) : Desktop.DockControlPalette.Surface(mode, 24));
        Glass.Configure(mode, material, optics, highlights, 1);
    }

    internal void SetOpen(bool open)
    {
        if (openTarget == open) return;
        openTarget = open;
        var reduced = !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        var fromCells = cells.Opacity; var fromSquare = square.Opacity;
        var fromWidth = expandingCell.Width; var fromHeight = expandingCell.Height;
        var fromRadiusX = expandingCell.RadiusX; var fromRadiusY = expandingCell.RadiusY;
        var remaining = cells.Children.OfType<Rectangle>().Skip(1).Select(cell => (Cell: cell, cell.Opacity)).ToArray();
        motion?.Stop();
        cells.Opacity = reduced && open ? 0 : 1; square.Opacity = reduced && open ? 1 : 0;
        expandingCell.Width = expandingCell.Height = !reduced && open ? 18 : 7;
        expandingCell.RadiusX = expandingCell.RadiusY = !reduced && open ? 3 : 1.8;
        foreach (var value in remaining) value.Cell.Opacity = !reduced && open ? 0 : 1;
        AutomationProperties.SetName(this, open ? "Close Quick Settings" : "Open Quick Settings");
        var next = new Storyboard(); motion = next;
        void Animate(DependencyObject element, string property, double from, double to)
        {
            var animation = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(reduced ? 100 : 300),
                EnableDependentAnimation = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(animation, element); Storyboard.SetTargetProperty(animation, property); next.Children.Add(animation);
        }
        Animate(cells, "Opacity", fromCells, cells.Opacity); Animate(square, "Opacity", fromSquare, square.Opacity);
        if (!reduced)
        {
            Animate(expandingCell, "Width", fromWidth, expandingCell.Width);
            Animate(expandingCell, "Height", fromHeight, expandingCell.Height);
            Animate(expandingCell, "RadiusX", fromRadiusX, expandingCell.RadiusX);
            Animate(expandingCell, "RadiusY", fromRadiusY, expandingCell.RadiusY);
            foreach (var value in remaining) Animate(value.Cell, "Opacity", value.Opacity, value.Cell.Opacity);
        }
        next.Completed += (_, _) => { if (ReferenceEquals(motion, next)) { next.Stop(); motion = null; } };
        next.Begin();
    }

    public void Dispose() { motion?.Stop(); Glass.Dispose(); }
}
