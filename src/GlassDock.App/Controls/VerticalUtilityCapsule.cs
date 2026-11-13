using System.Numerics;
using GlassDock.App.Desktop;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDock.App.Controls;

internal sealed class VerticalUtilityCapsule : IDisposable
{
    public UtilityHudCapsule View { get; }
    public UtilityGlassSurface Glass { get; } = new() { Circular = false };
    private readonly Func<UtilityHudValue> read;
    private readonly Action<float> write;
    private readonly Border fallback = new();
    private readonly Border fill = new() { VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
        Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(240, 252, 250, 245)) };
    private readonly UtilityIcon icon = new() { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 12) };
    private readonly Microsoft.UI.Composition.CompositionRoundedRectangleGeometry rounded;
    private readonly Microsoft.UI.Composition.CompositionGeometricClip clip;
    private Storyboard? filling;
    private bool dragging, disposed;
    private float? preview;
    private long previewUntil;
    private double targetHeight = -1;
    private UtilityTheme theme;

    internal VerticalUtilityCapsule(string name, Func<UtilityHudValue> read, Action<float> write)
    {
        this.read = read; this.write = write;
        View = new(name, read, write) { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), IsTabStop = true };
        View.Children.Add(fallback); View.Children.Add(Glass.Element); View.Children.Add(fill); View.Children.Add(icon);
        var visual = ElementCompositionPreview.GetElementVisual(View);
        rounded = visual.Compositor.CreateRoundedRectangleGeometry(); clip = visual.Compositor.CreateGeometricClip(rounded); visual.Clip = clip;
        View.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(View).Properties.IsLeftButtonPressed || !read().Available) return;
            dragging = View.CapturePointer(e.Pointer); SetFromPointer(e); e.Handled = true;
        };
        View.PointerMoved += (_, e) => { if (dragging) { SetFromPointer(e); e.Handled = true; } };
        View.PointerReleased += (_, e) => { if (!dragging) return; SetFromPointer(e); dragging = false; View.ReleasePointerCaptures(); e.Handled = true; };
        View.PointerCaptureLost += (_, _) => dragging = false;
        View.PointerCanceled += (_, _) => { dragging = false; View.ReleasePointerCaptures(); };
        View.KeyDown += (_, e) =>
        {
            var delta = e.Key is global::Windows.System.VirtualKey.Up or global::Windows.System.VirtualKey.Right ? .02f
                : e.Key is global::Windows.System.VirtualKey.Down or global::Windows.System.VirtualKey.Left ? -.02f : 0;
            if (delta == 0 || !read().Available) return;
            preview = Math.Clamp((preview ?? read().Level) + delta, 0, 1); previewUntil = Environment.TickCount64 + 3000;
            write(preview.Value); Update(); e.Handled = true;
        };
    }

    private void SetFromPointer(PointerRoutedEventArgs e)
    {
        preview = Math.Clamp(1 - (float)(e.GetCurrentPoint(View).Position.Y / Math.Max(1, View.ActualHeight)), 0, 1);
        previewUntil = Environment.TickCount64 + 3000; write(preview.Value); Update();
    }

    internal void Configure(double width, double height, DockAppearanceMode mode, UtilityTheme theme,
        GlassMaterial material, LiquidGlassMaterial optics, float scale, bool highlights)
    {
        this.theme = mode.GlassStyle() is null ? mode == DockAppearanceMode.Light ? UtilityTheme.Light : UtilityTheme.Dark : theme;
        View.Width = width; View.Height = height;
        rounded.Size = new((float)width, (float)height); rounded.CornerRadius = new((float)(width / 2));
        fallback.CornerRadius = new(width / 2);
        fallback.Background = new SolidColorBrush(mode.GlassStyle() is null ? DockControlPalette.SolidSurface(mode) : DockControlPalette.Surface(mode, 24));
        Glass.CornerRadius = (float)(width / 2); Glass.Configure(mode, material, optics, highlights, scale);
        Update();
    }

    internal void Update()
    {
        if (disposed) return;
        var actual = read();
        if (!dragging && preview is { } pending && (!actual.Available || Environment.TickCount64 >= previewUntil ||
            !actual.Pending && Math.Abs(actual.Level - pending) < .015f)) preview = null;
        var level = Math.Clamp(preview ?? actual.Level, 0, 1);
        View.IsHitTestVisible = View.IsTabStop = actual.Available;
        View.Opacity = actual.Available ? 1 : .5;
        ToolTipService.SetToolTip(View, actual.Status);
        _ = icon.SetAsync(actual.Icon, true, level > .15f ? 0xFF1C1C1Eu : UtilityColors.Icon(theme, false));
        var next = Math.Max(0, View.Height * level);
        if (Math.Abs(next - targetHeight) < .01) return;
        targetHeight = next;
        var from = fill.ActualHeight; filling?.Stop(); fill.Height = next;
        if (dragging || !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
        filling = new();
        var motion = new DoubleAnimation { From = from, To = next, Duration = TimeSpan.FromMilliseconds(100),
            EnableDependentAnimation = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(motion, fill); Storyboard.SetTargetProperty(motion, "Height"); filling.Children.Add(motion); filling.Begin();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; View.ReleasePointerCaptures(); filling?.Stop();
        ElementCompositionPreview.GetElementVisual(View).Clip = null;
        clip.Dispose(); rounded.Dispose(); Glass.Dispose();
    }
}
