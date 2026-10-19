using System.Numerics;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GlassDock.App.Desktop;

/// <summary>Retained lighting brushes; no capture, timers, or per-frame allocations.</summary>
internal sealed class ControlCenterDepth
{
    public Action<FrameworkElement>? RegisterGlass { get; set; }
    private readonly HashSet<Border> activeCards = [];
    private readonly HashSet<Border> raisedCards = [];
    private DockAppearanceMode mode;
    // Frosted white tint, with enough transmission for the live material below.
    private readonly SolidColorBrush clearActive = new(Color.FromArgb(238, 245, 245, 247));
    private Brush Active => mode == DockAppearanceMode.Clear ? clearActive : Blue;
    public SolidColorBrush Card { get; } = new();
    public LinearGradientBrush Edge { get; } = Gradient(false);
    public SolidColorBrush Hover { get; } = new();
    public SolidColorBrush Pressed { get; } = new();
    public SolidColorBrush Icon { get; } = new();
    public LinearGradientBrush ActiveIcon { get; } = Gradient(false);
    public SolidColorBrush Blue { get; } = new();
    private readonly SolidColorBrush activePressed = new(Color.FromArgb(238, 230, 230, 234));
    public LinearGradientBrush Track { get; } = Gradient(false);
    public LinearGradientBrush Fill { get; } = Gradient(true);

    private static LinearGradientBrush Gradient(bool horizontal) => new()
    {
        StartPoint = horizontal ? new(0, 0) : new(.5, 0),
        EndPoint = horizontal ? new(1, 0) : new(.5, 1),
        GradientStops = { new() { Offset = 0 }, new() { Offset = .48 }, new() { Offset = 1 } }
    };
    private static void Set(LinearGradientBrush brush, Color top, Color middle, Color bottom)
    { brush.GradientStops[0].Color = top; brush.GradientStops[1].Color = middle; brush.GradientStops[2].Color = bottom; }
    private static void Set(SolidColorBrush brush, Color top, Color middle, Color bottom) => brush.Color = top;
    private static Color C(byte a, byte value) => Color.FromArgb(a, value, value, value);

    public void Apply(DockAppearanceMode mode)
    {
        this.mode = mode;
        var light = mode == DockAppearanceMode.Light;
        var clear = mode == DockAppearanceMode.Clear;
        Set(Card, clear ? C(36, 255) : light ? C(135, 255) : C(42, 255),
            clear ? C(8, 255) : light ? C(75, 255) : C(14, 255),
            clear ? C(20, 255) : light ? C(105, 255) : C(24, 255));
        Set(Edge, light ? C(235, 255) : C(220, 255),
            light ? C(68, 255) : C(36, 255),
            light ? C(155, 255) : C(132, 255));
        Set(Hover, clear ? C(58, 255) : light ? C(165, 255) : C(68, 255),
            clear ? C(18, 255) : light ? C(95, 255) : C(22, 255),
            clear ? C(32, 255) : light ? C(130, 255) : C(36, 255));
        Set(Pressed, clear ? C(28, 255) : light ? C(105, 255) : C(32, 255),
            clear ? C(10, 255) : light ? C(60, 255) : C(12, 255),
            clear ? C(18, 255) : light ? C(85, 255) : C(20, 255));
        Set(Icon, clear ? C(50, 255) : light ? C(165, 255) : C(56, 255),
            clear ? C(14, 255) : light ? C(95, 255) : C(20, 255),
            clear ? C(30, 255) : light ? C(125, 255) : C(32, 255));
        Set(ActiveIcon, C(252, 255), C(240, 248), C(248, 255));
        Set(Blue, Color.FromArgb(clear ? (byte)138 : (byte)160, 76, 214, 255),
            Color.FromArgb(clear ? (byte)112 : (byte)136, 22, 146, 255),
            Color.FromArgb(clear ? (byte)132 : (byte)154, 10, 114, 244));
        Set(Track, clear ? C(46, 16) : light ? C(45, 50) : C(68, 16),
            clear ? C(34, 24) : light ? C(35, 70) : C(52, 24),
            clear ? C(28, 255) : light ? C(25, 100) : C(34, 255));
        Set(Fill, C(255, 244), C(255, 255), C(255, 240));
        // Clear gets the actual pill shader beneath XAML, without a second highlight layer.
        if (clear)
        {
            Set(Card, C(0, 0), C(0, 0), C(0, 0));
            Set(Edge, C(0, 255), C(0, 255), C(0, 255));
            Set(Icon, C(0, 255), C(0, 255), C(0, 255));
            Set(Hover, C(12, 255), C(12, 255), C(12, 255));
            Set(Pressed, C(16, 0), C(16, 0), C(16, 0));
        }
        else
        {
            var normal = DockControlPalette.Normal(mode, true);
            var hover = DockControlPalette.Hover(mode, true);
            var pressed = DockControlPalette.Pressed(mode, true);
            Set(Card, normal, normal, normal); Set(Icon, normal, normal, normal);
            Set(Hover, hover, hover, hover); Set(Pressed, pressed, pressed, pressed);
            var edge = DockControlPalette.Surface(mode, 24);
            Set(Edge, edge, edge, edge);
            var blue = light ? Color.FromArgb(255, 45, 99, 196) : Color.FromArgb(255, 47, 108, 219);
            Set(Blue, blue, blue, blue);
            if (light) Set(Fill, C(255, 36), C(255, 36), C(255, 36));
        }
        foreach (var card in activeCards) card.Background = Active;
    }

    public void SetActive(Border card, bool active)
    {
        SetTransition(card);
        if (active) activeCards.Add(card); else activeCards.Remove(card);
        card.Background = active ? Active : Card;
    }
    private Brush Resting(Border card) => activeCards.Contains(card) ? Active : Card;

    public void ResetInteraction()
    {
        // Navigation can hide a pressed/hovered card before its release/exit
        // event arrives. Do not restore those transient brushes on return.
        foreach (var card in raisedCards)
        {
            card.Background = Resting(card);
        }
    }

    public void Raise(Border card, bool interactive = false)
    {
        // Keep input-bearing cards in XAML's two-dimensional layout plane.
        // Their frosted depth is painted by the shared renderer; a Z translation
        // adds no shadow here and can separate Viewbox visuals from hit regions.
        raisedCards.Add(card);
        SetTransition(card);
        card.Background = Card; card.BorderBrush = Edge;
        RegisterGlass?.Invoke(card);
        card.Loaded += (_, _) => card.BorderThickness = new Thickness(.75 / (card.XamlRoot?.RasterizationScale ?? 1));
        card.BorderThickness = new Thickness(.75);
        if (!interactive) return;
        card.PointerEntered += (_, _) => { card.Background = activeCards.Contains(card) ? Active : Hover; };
        card.PointerExited += (_, _) => { card.Background = Resting(card); };
        card.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        { card.Background = activeCards.Contains(card) && mode == DockAppearanceMode.Clear ? activePressed : Pressed; }), true);
        card.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        { card.Background = Resting(card); }), true);
        card.PointerCanceled += (_, _) => { card.Background = Resting(card); };
        card.PointerCaptureLost += (_, _) => { card.Background = Resting(card); };
    }

    private static void SetTransition(Border card)
    {
        card.BackgroundTransition ??= new BrushTransition();
        card.BackgroundTransition.Duration = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled
            ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero;
    }

    public void StyleButton(Button button)
    {
        RegisterGlass?.Invoke(button);
        button.Background = Icon; button.BorderBrush = Edge; button.BorderThickness = new Thickness(.75);
        button.Loaded += (_, _) => button.BorderThickness = new Thickness(.75 / (button.XamlRoot?.RasterizationScale ?? 1));
        button.Resources["ButtonBackground"] = Icon;
        button.Resources["ButtonBackgroundPointerOver"] = Hover;
        button.Resources["ButtonBackgroundPressed"] = Pressed;
        button.Resources["ButtonBackgroundDisabled"] = Icon;
        button.Resources["ButtonBorderBrush"] = Edge;
        button.Resources["ButtonBorderBrushPointerOver"] = Edge;
        button.Resources["ButtonBorderBrushPressed"] = Edge;
        button.Resources["ButtonBorderBrushDisabled"] = Edge;
    }
}
