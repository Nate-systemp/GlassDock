using System.Numerics;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Desktop;
using GlassDock.Core.Applications;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDock.App.Desktop;

internal readonly record struct VolumeHudAnchor(nint Window, PreviewRect Dock, IReadOnlyList<PreviewRect> Utilities, PreviewRect? Sound);

/// <summary>One retained non-activating capsule; its host never resizes during a burst.</summary>
internal sealed class VolumeHudWindow : Window, IDisposable
{
    private const double CapsuleWidth = VolumeHudPlacement.Width, ExpandedHeight = VolumeHudPlacement.ExpandedHeight,
        CompactHeight = VolumeHudPlacement.CompactHeight;
    private const double HostWidth = VolumeHudPlacement.HostWidth, HostHeight = VolumeHudPlacement.HostHeight;
    private readonly Grid root = new() { Width = HostWidth, Height = HostHeight };
    private readonly VolumeHudCapsule capsule;
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, IsHitTestVisible = false };
    private readonly Grid fill = new() { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    private readonly UtilityIcon speaker = new() { Width = 16, Height = 16, Margin = new(12, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly RectangleGeometry fillClip = new();
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly PopupLiquidGlassSurface liquid;
    private readonly WindowPreviewPlacement placement;
    private readonly MasterVolumeService audio;
    private readonly VolumeHudState state = new();
    private readonly ScaleTransform springScale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly TranslateTransform springOffset = new();
    private readonly DispatcherQueueTimer idle;
    private readonly bool animations = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private readonly CompositionRoundedRectangleGeometry rounded;
    private readonly CompositionGeometricClip roundedClip;
    private Func<VolumeHudAnchor?>? anchor;
    private Storyboard? presentation, shape, filling;
    private double shapeTarget = ExpandedHeight, fillTarget, dpi = 1;
    private double layoutScale = 1;
    private double ContentWidth => CapsuleWidth * layoutScale;
    private double WindowWidth => HostWidth * layoutScale;
    private double WindowHeight => HostHeight * layoutScale;
    private int revision;
    private bool open, dragging, hovering, disposed, frozen;
    private DockAppearanceSettings? appliedAppearance;
    private DockAppearanceMode appliedMode;
    internal bool IsOpen => open;

    internal VolumeHudWindow(MasterVolumeService audio)
    {
        this.audio = audio;
        capsule = new(audio) { Width = CapsuleWidth, Height = ExpandedHeight,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new(0, 0, 0, VolumeHudPlacement.Padding) };
        Title = "Doky — Volume"; WindowBranding.Apply(this);
        AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        Content = root;
        root.Children.Add(glass); root.Children.Add(capsule);
        fill.Children.Add(new Border { Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 252, 250, 245)),
            Width = CapsuleWidth, HorizontalAlignment = HorizontalAlignment.Left });
        fill.Children.Add(speaker); fill.Clip = fillClip;
        capsule.Children.Add(fill);
        capsule.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var transforms = new TransformGroup(); transforms.Children.Add(springScale); transforms.Children.Add(springOffset);
        root.RenderTransform = transforms; root.RenderTransformOrigin = new(.5, (HostHeight - VolumeHudPlacement.Padding) / HostHeight);
        var visual = ElementCompositionPreview.GetElementVisual(capsule);
        rounded = visual.Compositor.CreateRoundedRectangleGeometry();
        roundedClip = visual.Compositor.CreateGeometricClip(rounded); visual.Clip = roundedClip;
        placement = new(WinRT.Interop.WindowNative.GetWindowHandle(this));
        SystemBackdrop = backdrop;
        liquid = new(this, root, backdrop) { UseDockOptics = true };
        idle = DispatcherQueue.CreateTimer(); idle.Interval = TimeSpan.FromSeconds(1.5); idle.IsRepeating = false;
        idle.Tick += (_, _) => Hide();
        capsule.SizeChanged += (_, _) => UpdateGeometry();
        fill.SizeChanged += (_, _) => UpdateFillClip();
        root.Loaded += (_, _) => UpdateGeometry();
        capsule.PointerEntered += (_, _) =>
        {
            hovering = true; idle.Stop();
            var hiding = state.Phase == VolumeHudPhase.Hidden;
            state.BeginInteraction();
            if (hiding) AnimatePresentation(false);
            AnimateShape();
        };
        capsule.PointerExited += (_, _) =>
        {
            hovering = false;
            if (!dragging) { state.EndInteraction(); RestartIdle(); }
        };
        capsule.PointerPressed += (_, e) =>
        {
            if (!audio.State.Available || !e.GetCurrentPoint(capsule).Properties.IsLeftButtonPressed) return;
            dragging = true; state.BeginInteraction(); idle.Stop(); AnimateShape();
            capsule.CapturePointer(e.Pointer); SetPointerVolume(e); e.Handled = true;
        };
        capsule.PointerMoved += (_, e) => { if (dragging) { SetPointerVolume(e); e.Handled = true; } };
        capsule.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            SetPointerVolume(e); dragging = false; capsule.ReleasePointerCapture(e.Pointer);
            EndInteraction(); e.Handled = true;
        };
        capsule.PointerCaptureLost += (_, _) => { dragging = false; EndInteraction(); };
        capsule.PointerCanceled += (_, _) => { dragging = false; EndInteraction(); };
        Closed += (_, _) => Cleanup();
        AutomationProperties.SetName(capsule, "Master volume");
    }

    internal void ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        if (appliedAppearance == appearance && appliedMode == mode) return;
        appliedAppearance = appearance; appliedMode = mode;
        root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        PopupStyle.Apply(glass, backdrop, appearance, mode, ExpandedHeight / 2);
        UpdateGeometry();
    }

    internal void Show(MasterVolumeState value, Func<VolumeHudAnchor?> source, bool explicitOpen)
    {
        if (disposed || !value.Available) return;
        var entering = state.Phase == VolumeHudPhase.Hidden;
        anchor = source; state.Change(explicitOpen);
        if (!open)
        {
            open = true;
            CompositionTarget.Rendering += Frame;
            if (animations) { root.Opacity = 0; springScale.ScaleX = .82; springScale.ScaleY = .65; springOffset.Y = 8; }
        }
        Reposition();
        if (entering) AnimatePresentation(false);
        AnimateShape(); Update(value); RestartIdle();
    }

    internal void Update(MasterVolumeState value)
    {
        if (disposed || !open) return;
        if (!value.Available) { Hide(true); return; }
        _ = speaker.SetAsync(value.Muted ? "speaker-x" : value.Volume < .4 ? "speaker-low" : "speaker-high", true, 0xFF99958B);
        AutomationProperties.SetItemStatus(capsule, value.Muted ? "Muted" : $"{Math.Round(value.Volume * 100)} percent");
        var target = ContentWidth * value.Volume;
        if (Math.Abs(fillTarget - target) < .01) return;
        fillTarget = target;
        var current = fill.Width; filling?.Stop(); fill.Width = target;
        if (!animations || frozen) return;
        filling = new(); Add(filling, fill, "Width", current, target, 110, new SineEase { EasingMode = EasingMode.EaseOut });
        filling.Begin();
    }

    private void SetPointerVolume(PointerRoutedEventArgs e) => audio.SetVolume((float)VolumeHudState.VolumeAt(e.GetCurrentPoint(capsule).Position.X, ContentWidth));
    private void EndInteraction()
    {
        if (hovering) return;
        state.EndInteraction(); RestartIdle();
    }
    private void RestartIdle()
    {
        idle.Stop();
        if (open && !state.Interacting && !frozen) idle.Start();
    }
    private void AnimateShape()
    {
        var compact = state.Phase == VolumeHudPhase.Compact;
        var target = (compact ? CompactHeight : ExpandedHeight) * layoutScale;
        if (shapeTarget == target) return;
        shapeTarget = target;
        var current = capsule.Height; var iconOpacity = speaker.Opacity; shape?.Stop();
        capsule.Height = target; speaker.Opacity = compact ? 0 : 1;
        if (!animations) { UpdateGeometry(); return; }
        shape = new();
        Add(shape, capsule, "Height", current, target, 270, new SineEase { EasingMode = EasingMode.EaseInOut });
        Add(shape, speaker, "Opacity", iconOpacity, speaker.Opacity, 140, new SineEase { EasingMode = EasingMode.EaseOut });
        shape.Begin();
    }
    private void AnimatePresentation(bool hiding)
    {
        var current = (root.Opacity, springScale.ScaleX, springScale.ScaleY, springOffset.Y);
        var generation = ++revision; presentation?.Stop();
        root.Opacity = 1; springScale.ScaleX = hiding ? 0 : 1;
        springScale.ScaleY = hiding ? 0 : 1; springOffset.Y = hiding ? 6 * layoutScale : 0;
        if (!animations) { if (hiding) FinishHide(); return; }
        presentation = new();
        var spring = new BackEase { Amplitude = hiding ? .6 : 1, EasingMode = hiding ? EasingMode.EaseIn : EasingMode.EaseOut };
        if (!hiding)
            Add(presentation, root, "Opacity", current.Opacity, root.Opacity, 160, new SineEase { EasingMode = EasingMode.EaseInOut });
        Add(presentation, springScale, "ScaleX", current.ScaleX, springScale.ScaleX, hiding ? 230 : 350, spring);
        Add(presentation, springScale, "ScaleY", current.ScaleY, springScale.ScaleY, hiding ? 230 : 350, spring);
        Add(presentation, springOffset, "Y", current.Y, springOffset.Y, hiding ? 230 : 350, spring);
        presentation.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (disposed || generation != revision) return;
            if (hiding) FinishHide();
            else { presentation?.Stop(); UpdateGeometry(); }
        });
        presentation.Begin();
    }
    private static void Add(Storyboard story, DependencyObject target, string property, double from, double to, int milliseconds, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EnableDependentAnimation = true, EasingFunction = easing };
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); story.Children.Add(animation);
    }
    private void Reposition()
    {
        if (!open || anchor?.Invoke() is not { } source) { Hide(true); return; }
        var (area, scale, host) = WindowPreviewPlacement.GetArea(source.Window); dpi = scale;
        var fit = Math.Min(1, Math.Min(area.Width / dpi / HostWidth, area.Height / dpi / HostHeight));
        if (fit <= 0) { Hide(true); return; }
        if (Math.Abs(fit - layoutScale) > .001)
        {
            shape?.Stop(); filling?.Stop(); layoutScale = fit;
            root.Width = WindowWidth; root.Height = WindowHeight; capsule.Width = ContentWidth;
            capsule.Margin = new(0, 0, 0, VolumeHudPlacement.Padding * fit);
            shapeTarget = (state.Phase == VolumeHudPhase.Compact ? CompactHeight : ExpandedHeight) * fit;
            capsule.Height = shapeTarget;
            speaker.Width = speaker.Height = 16 * fit; speaker.Margin = new(12 * fit, 0, 0, 0);
            fillTarget = ContentWidth * audio.State.Volume; fill.Width = fillTarget;
        }
        PreviewRect Physical(PreviewRect rect) => new(host.X + rect.X * dpi, host.Y + rect.Y * dpi, rect.Width * dpi, rect.Height * dpi);
        var rectangle = VolumeHudPlacement.Position(area, Physical(source.Dock), source.Utilities.Select(Physical).ToArray(),
            source.Sound is { } sound ? Physical(sound) : null, dpi * layoutScale);
        if (rectangle is not { } position) { Hide(true); return; }
        placement.Position(position);
    }
    private void Frame(object? sender, object args)
    {
        if (disposed || !open) return;
        Reposition(); UpdateGeometry();
    }
    private void UpdateGeometry()
    {
        if (disposed) return;
        var height = Math.Max(1, capsule.Height);
        var bottom = VolumeHudPlacement.Padding * layoutScale;
        glass.Width = ContentWidth; glass.Height = height;
        glass.HorizontalAlignment = HorizontalAlignment.Center; glass.VerticalAlignment = VerticalAlignment.Bottom;
        glass.Margin = new(0, 0, 0, bottom);
        rounded.Size = new((float)ContentWidth, (float)height); rounded.CornerRadius = new((float)(height / 2));
        UpdateFillClip();
        backdrop.SetBounds(WindowWidth, WindowHeight, ContentWidth, height, bottom, dpi, 1);
        var matrix = Matrix4x4.CreateScale((float)springScale.ScaleX, (float)springScale.ScaleY, 1, new((float)(WindowWidth / 2), (float)(WindowHeight - bottom), 0))
            * Matrix4x4.CreateTranslation(0, (float)springOffset.Y, 0);
        backdrop.SetPresentationTransform(matrix, root.Opacity);
    }
    private void UpdateFillClip() => fillClip.Rect = new(0, 0, Math.Max(0, fill.Width), Math.Max(1, capsule.Height));
    internal void Hide(bool immediate = false)
    {
        if (!open || disposed || !immediate && !state.Hide()) return;
        if (immediate) { state.EndInteraction(); state.Hide(); FinishHide(); return; }
        idle.Stop(); AnimatePresentation(true);
    }
    private void FinishHide()
    {
        if (!open) return;
        revision++; open = false; dragging = hovering = false;
        idle.Stop(); presentation?.Stop(); shape?.Stop(); filling?.Stop();
        capsule.ReleasePointerCaptures();
        state.EndInteraction(); state.Hide();
        CompositionTarget.Rendering -= Frame;
        placement.Hide();
    }
    internal void BeginScreenshot()
    {
        if (!open || disposed) return;
        frozen = true; idle.Stop();
        var current = (root.Opacity, springScale.ScaleX, springScale.ScaleY, springOffset.Y, capsule.Height, speaker.Opacity, fill.Width);
        revision++; presentation?.Stop(); shape?.Stop(); filling?.Stop();
        root.Opacity = current.Item1; springScale.ScaleX = current.ScaleX; springScale.ScaleY = current.ScaleY; springOffset.Y = current.Y;
        capsule.Height = current.Height; speaker.Opacity = current.Item6; fill.Width = current.Width;
        UpdateGeometry(); liquid.BeginScreenshotMode();
    }
    internal void EndScreenshot()
    {
        if (!frozen || disposed) return;
        liquid.EndScreenshotMode(); frozen = false;
        if (state.Phase == VolumeHudPhase.Hidden) { FinishHide(); return; }
        shapeTarget = -1; AnimateShape(); AnimatePresentation(false); RestartIdle();
    }
    private void Cleanup()
    {
        if (disposed) return;
        FinishHide(); disposed = true;
        liquid.Dispose(); placement.Dispose(); roundedClip.Dispose(); rounded.Dispose(); SystemBackdrop = null;
    }
    public void Dispose() { if (disposed) return; Cleanup(); Close(); }
}

internal sealed class VolumeHudCapsule(MasterVolumeService audio) : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new VolumeHudAutomationPeer(this, audio);
}

internal sealed class VolumeHudAutomationPeer(VolumeHudCapsule owner, MasterVolumeService audio)
    : FrameworkElementAutomationPeer(owner), IRangeValueProvider
{
    protected override string GetClassNameCore() => "DokyVolumeCapsule";
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    protected override bool IsControlElementCore() => true;
    protected override object GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPatternCore(pattern);
    public bool IsReadOnly => !audio.State.Available;
    public double LargeChange => 10;
    public double SmallChange => 2;
    public double Maximum => 100;
    public double Minimum => 0;
    public double Value => audio.State.Volume * 100;
    public void SetValue(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
        if (IsReadOnly) throw new InvalidOperationException("Audio output unavailable.");
        audio.SetVolume((float)(value / 100));
    }
}
