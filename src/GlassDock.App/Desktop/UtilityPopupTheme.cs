using GlassDock.Core.Settings;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace GlassDock.App.Desktop;

/// <summary>Retained brushes derived from the dock's own interaction palette.</summary>
internal sealed class UtilityPopupTheme
{
    public SolidColorBrush Primary { get; } = new();
    public SolidColorBrush Secondary { get; } = new();
    public SolidColorBrush Muted { get; } = new();
    public SolidColorBrush Tile { get; } = new();
    public SolidColorBrush TileBorder { get; } = new();
    public SolidColorBrush Overlay { get; } = new();
    public SolidColorBrush ReadingSurface { get; } = new();
    public SolidColorBrush Divider { get; } = new();
    public SolidColorBrush Accent { get; } = new();
    public SolidColorBrush AccentFill { get; } = new();
    public SolidColorBrush AccentText { get; } = new();
    public SolidColorBrush Hover { get; } = new();
    public SolidColorBrush Pressed { get; } = new();
    public SolidColorBrush Track { get; } = new();
    public DockAppearanceMode Mode { get; private set; }

    public UtilityPopupTheme() => Apply(DockAppearanceMode.Dark);

    public void ApplyReadingContrast(double opacity, DockAppearanceMode? materialMode = null)
    {
        var surfaceMode = materialMode ?? Mode;
        var alpha = GlassDock.Core.Materials.UtilityMaterial.DashboardContrast(surfaceMode, opacity);
        // Dark glass already gets tint from the native dock graph. Stacking a
        // charcoal XAML wash and a black shadow over that graph muddies it.
        ReadingSurface.Color = DockControlPalette.SolidSurface(Mode) with
        { A = surfaceMode.GlassStyle() is null ? (byte)255 :
            Mode == DockAppearanceMode.Light ? (byte)Math.Round(alpha * 255) : (byte)0 };
    }

    public void StyleButton(Button button)
    {
        button.CornerRadius = new CornerRadius(DockControlPalette.ButtonRadius);
        button.Foreground = Primary;
        button.Resources["ButtonBackgroundPointerOver"] = Hover;
        button.Resources["ButtonBackgroundPressed"] = Pressed;
        button.Resources["ButtonForegroundPointerOver"] = Primary;
        button.Resources["ButtonForegroundPressed"] = Primary;
        button.Resources["ButtonBorderBrushPointerOver"] = TileBorder;
        button.Resources["ButtonBorderBrushPressed"] = TileBorder;
    }

    public void Apply(DockAppearanceMode mode)
    {
        Mode = mode;
        var foreground = DockControlPalette.Foreground(mode);
        Primary.Color = foreground;
        Secondary.Color = foreground with { A = 210 };
        Muted.Color = foreground with { A = 165 };
        Tile.Color = DockControlPalette.Normal(mode, true);
        TileBorder.Color = DockControlPalette.Surface(mode, 24);
        Hover.Color = DockControlPalette.Hover(mode, true);
        Pressed.Color = DockControlPalette.Pressed(mode, true);
        AccentFill.Color = DockControlPalette.Pressed(mode, true);
        Accent.Color = foreground;
        AccentText.Color = foreground;
        Divider.Color = foreground with { A = 48 };
        Track.Color = DockControlPalette.Surface(mode, 70);
        // No second material scrim above native glass. Solid modes use the
        // same neutral finishes as the main dock.
        Overlay.Color = mode switch
        {
            DockAppearanceMode.Light or DockAppearanceMode.Dark => DockControlPalette.SolidSurface(mode),
            _ => Color.FromArgb(0, 0, 0, 0)
        };
    }
}
