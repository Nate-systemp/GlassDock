using System.Numerics;
using GlassDock.App.Desktop;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDock.App.Controls;

internal sealed class QuickSettingsPanel : Canvas, IUtilityPanel
{
    internal static readonly UtilityKind[] CircleOrder = [UtilityKind.Wifi, UtilityKind.Bluetooth, UtilityKind.AirplaneMode,
        UtilityKind.BatterySaver, UtilityKind.NightLight, UtilityKind.Focus];
    private readonly List<(UtilityButton Button, Border Host)> circles = [];
    private readonly List<VerticalUtilityCapsule> capsules = [];
    private readonly VerticalUtilityCapsule? brightnessCapsule;
    private readonly List<UtilityGlassSurface> surfaces = [];
    private readonly Canvas capsuleCanvas = new();
    private readonly Border capsuleHost;
    private readonly Motion presentation = new(), capsuleMotion = new();
    private readonly DockAppearanceMode mode;
    private readonly UtilityTheme theme;
    private readonly double diameter, iconSize;
    private readonly float scale;
    private GlassMaterial material = new();
    private LiquidGlassMaterial optics = new();
    private bool highlights, disposed;
    private double availableHeight = 1000, columnX, rowHeight;
    private int capsuleRevision;
    private Vector2 origin;
    public FrameworkElement View => this;
    public IReadOnlyList<UtilityGlassSurface> GlassSurfaces => surfaces;
    public int GlassTargetsRevision => 0;
    public event EventHandler? GlassChanged;
    internal event EventHandler? DismissRequested;
    internal event EventHandler? BrightnessMonitorRequested;
    internal FrameworkElement? BrightnessAnchor => brightnessCapsule?.View;
    internal Vector4 ColumnBounds => new(origin.X + (float)columnX, origin.Y, (float)rowHeight, (float)Height);
    internal IEnumerable<UtilityGlassSurface> CapsuleSurfaces => capsules.Select(c => c.Glass);

    internal QuickSettingsPanel(IReadOnlyDictionary<UtilityKind, UtilityButton> buttons, UtilityKind[] selected,
        DockAppearanceMode mode, UtilityTheme theme, double diameter, double iconSize, float scale,
        Func<UtilityHudValue> volume, Action<float> setVolume, Func<UtilityHudValue> brightness, Action<float> setBrightness)
    {
        this.mode = mode; this.theme = theme; this.diameter = diameter * .85; this.iconSize = iconSize * .85; this.scale = scale;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, this) || ReferenceEquals(e.OriginalSource, capsuleCanvas))
                DismissRequested?.Invoke(this, EventArgs.Empty);
        };
        capsuleHost = Wrap(capsuleCanvas); Children.Add(capsuleHost);
        if (selected.Contains(UtilityKind.Volume)) capsules.Add(new("Master volume", volume, setVolume));
        if (selected.Contains(UtilityKind.Brightness))
        {
            brightnessCapsule = new("Display brightness", brightness, setBrightness); capsules.Add(brightnessCapsule);
            brightnessCapsule.View.ContextRequested += (_, e) => { e.Handled = true; BrightnessMonitorRequested?.Invoke(this, EventArgs.Empty); };
        }
        foreach (var capsule in capsules) { capsuleCanvas.Children.Add(capsule.View); surfaces.Add(capsule.Glass); }
        foreach (var kind in CircleOrder.Where(selected.Contains))
        {
            if (!buttons.TryGetValue(kind, out var button)) continue;
            button.SetFloatingPresentation(true);
            var host = Wrap(button); circles.Add((button, host)); Children.Add(host); surfaces.Add(button.Glass);
            button.GeometryChanged += ChildGeometryChanged;
        }
        LayoutControls();
    }

    private static Border Wrap(UIElement child) => new()
    {
        Child = child, Opacity = 0, RenderTransformOrigin = new(.5, .5),
        RenderTransform = new CompositeTransform { ScaleX = .86, ScaleY = .86, TranslateY = 10 }
    };
    private void ChildGeometryChanged(object? sender, EventArgs args) => GlassChanged?.Invoke(this, EventArgs.Empty);
    private void AnimationFrame(object? sender, object args) => GlassChanged?.Invoke(this, EventArgs.Empty);

    private void LayoutControls()
    {
        var gap = 8d * scale;
        rowHeight = circles.Count == 0 ? diameter : Math.Min(diameter, Math.Max(12, (availableHeight - gap * (circles.Count - 1)) / circles.Count));
        Height = Math.Min(availableHeight, Math.Max(170 * scale, circles.Count * rowHeight + Math.Max(0, circles.Count - 1) * gap));
        var capsuleWidth = 36d;
        capsuleCanvas.Width = capsuleHost.Width = capsules.Count * capsuleWidth + Math.Max(0, capsules.Count - 1) * gap;
        capsuleCanvas.Height = capsuleHost.Height = Height;
        columnX = capsules.Count == 0 ? 0 : capsuleHost.Width + (circles.Count == 0 ? 0 : gap);
        Width = Math.Max(36, columnX + (circles.Count == 0 ? 0 : rowHeight));
        for (var i = 0; i < capsules.Count; ++i)
        {
            capsules[i].Configure(capsuleWidth, Height, mode, theme, material, optics, scale, highlights);
            Canvas.SetLeft(capsules[i].View, i * (capsuleWidth + gap));
        }
        for (var i = 0; i < circles.Count; ++i)
        {
            var (button, host) = circles[i];
            var size = rowHeight * (button.Definition.Kind is UtilityKind.Wifi or UtilityKind.Bluetooth ? .96 : 1);
            host.Width = host.Height = size;
            button.Configure(size, iconSize, theme, mode, size);
            button.Glass.Configure(mode, material, optics, highlights, scale);
            Canvas.SetLeft(host, columnX + (rowHeight - size) / 2);
            Canvas.SetTop(host, Height - circles.Count * rowHeight - Math.Max(0, circles.Count - 1) * gap + i * (rowHeight + gap));
        }
        capsuleHost.Visibility = capsules.Count == 0 ? Visibility.Collapsed : capsuleHost.Visibility;
    }

    public void SetAvailableHeight(double height)
    {
        height = Math.Max(1, height);
        if (Math.Abs(availableHeight - height) < .5) return;
        availableHeight = height; LayoutControls();
    }
    public void ConfigureGlass(GlassMaterial material, LiquidGlassMaterial optics, float scale, bool highlights)
    {
        this.material = material; this.optics = optics; this.highlights = highlights; LayoutControls();
    }
    public Task RefreshAsync() { RefreshValues(); return Task.CompletedTask; }
    internal void RefreshValues() { foreach (var capsule in capsules) capsule.Update(); }

    public bool PositionGlass(FrameworkElement relativeTo, Vector2 origin, bool active)
    {
        this.origin = origin + new Vector2((float)UtilityPopupWindow.Gutter);
        var changed = false;
        foreach (var (button, _) in circles) changed |= Position(button, button.Glass);
        foreach (var capsule in capsules) changed |= Position(capsule.View, capsule.Glass);
        return changed;

        bool Position(FrameworkElement element, UtilityGlassSurface glass)
        {
            var rect = element.TransformToVisual(relativeTo).TransformBounds(new(0, 0, element.Width, element.Height));
            var next = new Vector4(origin.X + (float)rect.X, origin.Y + (float)rect.Y, (float)element.Width, (float)element.Height);
            var visible = active && mode.GlassStyle() is not null && element.IsLoaded && element.Visibility == Visibility.Visible &&
                (element.Parent is not FrameworkElement parent || parent.Visibility == Visibility.Visible);
            changed |= glass.Bounds != next || glass.Active != visible;
            glass.Bounds = next; glass.Active = visible;
            return changed;
        }
    }

    internal async Task AnimateAsync(bool hide)
    {
        capsuleRevision++; capsuleMotion.Stop();
        var controls = circles.Select(c => c.Host).ToList();
        if (capsules.Count > 0 && capsuleHost.Visibility == Visibility.Visible) controls.Insert(0, capsuleHost);
        CompositionTarget.Rendering -= AnimationFrame; CompositionTarget.Rendering += AnimationFrame;
        try { await presentation.Play(controls, hide, stagger: true); }
        finally { if (!presentation.Running && !capsuleMotion.Running) CompositionTarget.Rendering -= AnimationFrame; }
    }

    internal async Task ShowCapsulesAsync(bool show)
    {
        var current = ++capsuleRevision;
        if (capsules.Count == 0) return;
        capsuleHost.Visibility = Visibility.Visible;
        CompositionTarget.Rendering -= AnimationFrame; CompositionTarget.Rendering += AnimationFrame;
        await capsuleMotion.Play(new[] { capsuleHost }, !show, stagger: false);
        if (!disposed && current == capsuleRevision && !show) capsuleHost.Visibility = Visibility.Collapsed;
        GlassChanged?.Invoke(this, EventArgs.Empty);
        if (!presentation.Running && !capsuleMotion.Running) CompositionTarget.Rendering -= AnimationFrame;
    }

    internal void ReleaseButtons()
    {
        foreach (var (button, host) in circles)
        {
            button.GeometryChanged -= ChildGeometryChanged;
            host.Child = null; button.SetFloatingPresentation(false); button.HighlightSource(false);
        }
        circles.Clear();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; capsuleRevision++; presentation.Stop(); capsuleMotion.Stop(); CompositionTarget.Rendering -= AnimationFrame;
        ReleaseButtons(); foreach (var capsule in capsules) capsule.Dispose();
    }

    private sealed class Motion
    {
        private Storyboard? story;
        private TaskCompletionSource? completion;
        private IReadOnlyList<Border> activeControls = Array.Empty<Border>();
        internal bool Running => completion is { Task.IsCompleted: false };
        internal void Stop()
        {
            var values = activeControls.Select(c => (Control: c, c.Opacity, Transform: (CompositeTransform)c.RenderTransform,
                X: ((CompositeTransform)c.RenderTransform).ScaleX, Y: ((CompositeTransform)c.RenderTransform).TranslateY)).ToArray();
            story?.Stop(); story = null;
            foreach (var value in values)
            {
                value.Control.Opacity = value.Opacity;
                value.Transform.ScaleX = value.Transform.ScaleY = value.X; value.Transform.TranslateY = value.Y;
            }
            activeControls = Array.Empty<Border>(); completion?.TrySetResult(); completion = null;
        }
        internal Task Play(IReadOnlyList<Border> controls, bool hide, bool stagger)
        {
            var sampled = controls.Select(c => (c.Opacity, Transform: (CompositeTransform)c.RenderTransform,
                X: ((CompositeTransform)c.RenderTransform).ScaleX, Y: ((CompositeTransform)c.RenderTransform).TranslateY)).ToArray();
            Stop();
            activeControls = controls;
            var reduced = !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            var next = new Storyboard(); story = next;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); completion = done;
            for (var i = 0; i < controls.Count; ++i)
            {
                var element = controls[i]; var from = sampled[i];
                var delay = reduced || !stagger ? 0 : (hide ? controls.Count - 1 - i : i) * (hide ? 25 : 40);
                element.Opacity = hide ? 0 : 1;
                Add(element, "Opacity", from.Opacity, element.Opacity, opacity: true);
                from.Transform.ScaleX = from.Transform.ScaleY = reduced || !hide ? 1 : .86;
                from.Transform.TranslateY = reduced || !hide ? 0 : 10;
                if (!reduced)
                {
                    Add(from.Transform, "ScaleX", from.X, from.Transform.ScaleX);
                    Add(from.Transform, "ScaleY", from.X, from.Transform.ScaleY);
                    Add(from.Transform, "TranslateY", from.Y, from.Transform.TranslateY);
                }
                void Add(DependencyObject target, string property, double start, double end, bool opacity = false)
                {
                    var duration = reduced ? 100 : hide ? 140 : 260;
                    var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(delay + duration), EnableDependentAnimation = true };
                    animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = start });
                    if (delay > 0) animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay)), Value = start });
                    animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + duration)), Value = end,
                        EasingFunction = !hide && !opacity ? new BackEase { Amplitude = .3, EasingMode = EasingMode.EaseOut }
                            : new CubicEase { EasingMode = hide ? EasingMode.EaseIn : EasingMode.EaseOut } });
                    Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); next.Children.Add(animation);
                }
            }
            if (next.Children.Count == 0) { done.TrySetResult(); return done.Task; }
            next.Completed += (_, _) => { if (ReferenceEquals(story, next)) { next.Stop(); story = null; activeControls = Array.Empty<Border>(); } done.TrySetResult(); };
            next.Begin(); return done.Task;
        }
    }
}
