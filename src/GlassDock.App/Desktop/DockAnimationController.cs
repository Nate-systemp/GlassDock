using System.Numerics;
using GlassDock.App.Controls;
using GlassDock.Core.Settings;
using GlassDock.Core.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using global::Windows.UI.ViewManagement;

namespace GlassDock.App.Desktop;

internal sealed class DockAnimationController : IDisposable
{
    private double maximumMagnificationScale = GlassDockSettings.DefaultMagnificationScale;
    private const double MagnificationSigma = 52;
    private const double MagnificationEpsilon = 0.0015;
    private const double MagnificationSmoothing = 0.34;

    private readonly GlassSurface surface;
    private readonly Panel icons;
    private readonly DockIconTransition iconTransition;
    private readonly FrameworkElement indicator;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer magnificationTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer placementTimer;
    private double placementFrom, placementTarget, placementStarted, placementDuration;
    public bool IsPlacementAnimating { get; private set; }
    public event EventHandler? PlacementChanged;

    private Storyboard? active;
    private Storyboard? indicatorFade;
    private TaskCompletionSource<bool>? completion;
    private bool disposed;
    private double expandedWidth = 560, expandedHeight = 68;
    private double pathStartProgress, pathStartBottom, pathTargetBottom;
    private double collapsedBottom;
    private bool pathExpanded;
    // A built-in XAML dependency property provides the one storyboard clock.
    // It is never inserted into the visual tree and cannot affect hit testing.
    private readonly Slider progressClock = new() { Minimum = 0, Maximum = 1 };
    private double Progress { get => progressClock.Value; set => progressClock.Value = value; }
    private void ProgressChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args) => ApplyProgress();

    private void ApplyProgress()
    {
        if (disposed) return;
        var p = Math.Clamp(Progress, 0, 1);
        var frame = DockTransitionGeometry.At(p, expandedWidth, expandedHeight);
        surface.Width = indicator.Width = frame.Width;
        surface.Height = indicator.Height = frame.Height;
        if (indicator is Border pill) pill.CornerRadius = new CornerRadius(frame.CornerRadius);
        var remaining = (pathExpanded ? 1 : 0) - pathStartProgress;
        var fraction = Math.Abs(remaining) < .000001 ? 1 : Math.Clamp((p - pathStartProgress) / remaining, 0, 1);
        var bottom = pathStartBottom + (pathTargetBottom - pathStartBottom) * fraction;
        icons.Margin = new Thickness(0, 0, 0, bottom);
        ApplyBottom(bottom);
        // The retained pill and material share a silhouette, including during blending.
        indicator.Opacity = 1 - frame.MaterialBlend;
        surface.Opacity = frame.MaterialBlend;
    }

    private bool magnificationRequested;
    private bool interactionHeld;
    private bool magnificationTimerRunning;
    private double magnificationPointerX;

    public DockAnimationController(
        GlassSurface surface,
        Panel icons,
        FrameworkElement indicator)
    {
        progressClock.ValueChanged += ProgressChanged;
        this.surface = surface;
        this.icons = icons;
        iconTransition = new(icons, surface);
        this.indicator = indicator;

        magnificationTimer =
            icons.DispatcherQueue.CreateTimer();

        magnificationTimer.Interval =
            TimeSpan.FromMilliseconds(16);

        magnificationTimer.Tick +=
            (_, _) => TickMagnification();
        placementTimer = icons.DispatcherQueue.CreateTimer();
        placementTimer.Interval = TimeSpan.FromMilliseconds(16);
        placementTimer.Tick += (_, _) =>
        {
            if (disposed) return;
            var t = Math.Clamp((Environment.TickCount64 - placementStarted) / placementDuration, 0, 1);
            if (t == 1) { placementTimer.Stop(); IsPlacementAnimating = false; }

            // Directional easing, deliberately not ease-in-out:
            // raising/opening settles with sine ease-out;
            // lowering/closing folds away with sine ease-in.
            var eased = placementTarget >= placementFrom
                ? Math.Sin(t * Math.PI / 2)
                : 1 - Math.Cos(t * Math.PI / 2);

            ApplyBottom(placementFrom + (placementTarget - placementFrom) * eased);
        };
    }

    public void SetBottom(double bottom)
    {
        placementTimer.Stop(); IsPlacementAnimating = false;
        ApplyBottom(bottom);
    }

    public void AnimateBottom(double bottom, double milliseconds)
    {
        if (disposed) return;
        if (!new UISettings().AnimationsEnabled || milliseconds <= 0)
        {
            SetBottom(bottom);
            return;
        }
        placementFrom = surface.Margin.Bottom;
        placementTarget = bottom;
        placementStarted = Environment.TickCount64;
        placementDuration = milliseconds;
        IsPlacementAnimating = true;
        placementTimer.Start();
    }

    private void ApplyBottom(double bottom)
    {
        surface.Margin = indicator.Margin = new Thickness(0, 0, 0, bottom);
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fades the collapsed indicator without interrupting dock animation.</summary>
    public void AnimateIndicatorOpacity(double target, double milliseconds)
    {
        if (disposed) return;
        var from = indicator.Opacity;
        indicatorFade?.Stop();
        indicatorFade = null;
        indicator.Opacity = from;
        if (!new UISettings().AnimationsEnabled)
        {
            indicator.Opacity = target;
            return;
        }
        if (Math.Abs(from - target) < 0.001)
        {
            indicator.Opacity = target;
            return;
        }

        var storyboard = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = from,
            To = target,
            Duration = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fade, indicator);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
        indicatorFade = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (ReferenceEquals(indicatorFade, storyboard)) indicatorFade = null;
        };
        storyboard.Begin();
    }

    public void StopIndicatorOpacityAnimation()
    {
        var opacity = indicator.Opacity;
        indicatorFade?.Stop();
        indicatorFade = null;
        indicator.Opacity = opacity;
    }

    public void HoldMagnification(bool held)
    {
        interactionHeld = held;
        if (held) StopMagnificationTimer();
        else StartMagnificationTimer();
    }

    /// <summary>Resize a settled open dock without interpreting removed slots as collapse progress.</summary>
    public void ResizeExpanded(double width, double height)
    {
        if (disposed) return;
        expandedWidth = width;
        expandedHeight = height;
        iconTransition.SetExpandedSize(width, height);
        surface.Width = indicator.Width = width;
        surface.Height = indicator.Height = height;
    }

    public void SetMaximumMagnificationScale(double value)
    {
        maximumMagnificationScale = double.IsFinite(value)
            ? Math.Clamp(
                value,
                GlassDockSettings.MinimumMagnificationScale,
                GlassDockSettings.MaximumMagnificationScale)
            : GlassDockSettings.DefaultMagnificationScale;

        if (maximumMagnificationScale == 1)
            ResetMagnification(immediate: true);
        else if (magnificationRequested)
            StartMagnificationTimer();
    }

    public Task<bool> AnimateAsync(
        bool expanded,
        double targetWidth = 560,
        double targetHeight = 68,
        double? bottom = null)
    {
        if (!icons.DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Dock transitions must run on the UI thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
        var started =
            System.Diagnostics.Stopwatch.StartNew();

        Trace(
            $"AnimateAsync({expanded}, targetWidth={targetWidth}), previousPending={completion is { Task.IsCompleted: false }}");

        // Sample the rendered geometry before releasing its single animation clock.
        var progress = DockTransitionTiming.Progress(surface.Width, surface.Height, targetWidth, targetHeight);
        var currentBottom = surface.Margin.Bottom;
        if (expanded && progress <= .000001) collapsedBottom = currentBottom;
        var previous = active;
        active = null;
        previous?.Stop();
        completion?.TrySetResult(false);
        completion = null;
        StopIndicatorOpacityAnimation();
        placementTimer.Stop();
        IsPlacementAnimating = false;

        expandedWidth = targetWidth;
        expandedHeight = targetHeight;
        pathStartProgress = progress;
        pathStartBottom = currentBottom;
        pathTargetBottom = expanded ? bottom ?? currentBottom : collapsedBottom;
        pathExpanded = expanded;
        iconTransition.SetExpandedSize(targetWidth, targetHeight);
        Progress = progress;
        ApplyProgress();

        var animationsEnabled = new UISettings().AnimationsEnabled;
        Trace($"AnimationsEnabled={animationsEnabled}");
        if (!animationsEnabled)
        {
            ResetMagnification(immediate: true);
            Progress = expanded ? 1 : 0;
            ApplyProgress();
            return Task.FromResult(true);
        }

        if (!expanded) ResetMagnification();
        active = new Storyboard();
        var duration = (int)Math.Round(DockTransitionTiming.Duration(expanded, progress));
        Add(progressClock, "Value", progress, EasingMode.EaseInOut, (duration, expanded ? 1 : 0));
        var pending =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        completion = pending;

        var storyboard = active;
        storyboard.Completed +=
            (_, _) =>
            {
                if (!ReferenceEquals(active, storyboard) || !ReferenceEquals(completion, pending)) return;
                Trace(
                    $"Completed({expanded}) elapsed={started.ElapsedMilliseconds}ms width={surface.ActualWidth} height={surface.ActualHeight} icons={icons.Opacity}");

                pending.TrySetResult(true);
            };

        Trace(
            $"Begin({expanded})");

        storyboard.Begin();

        return pending.Task;
    }

    /// <summary>
    /// Updates the continuous macOS-style magnification field.
    /// The hovered icon grows the most and nearby icons grow progressively less.
    /// </summary>
    public void UpdateMagnification(
        double pointerX)
    {
        if (!double.IsFinite(pointerX))
            return;

        magnificationPointerX = pointerX;
        magnificationRequested = true;

        StartMagnificationTimer();
    }

    /// <summary>
    /// Smoothly restores all dock icons to their normal size.
    /// </summary>
    public void ResetMagnification(
        bool immediate = false)
    {
        magnificationRequested = false;

        if (immediate)
        {
            StopMagnificationTimer();

            foreach (var element in IconElements())
            {
                var visual =
                    ElementCompositionPreview.GetElementVisual(
                        element);

                ConfigureMagnificationOrigin(
                    element,
                    visual);

                visual.Scale =
                    Vector3.One;

                Canvas.SetZIndex(
                    element,
                    0);
            }

            return;
        }

        StartMagnificationTimer();
    }

    private void TickMagnification()
    {
        if (disposed) return;
        var settled = true;

        foreach (var element in IconElements())
        {
            var visual =
                ElementCompositionPreview.GetElementVisual(
                    element);

            ConfigureMagnificationOrigin(
                element,
                visual);

            var targetScale =
                magnificationRequested
                    ? TargetScaleFor(
                        element)
                    : 1.0;

            var currentScale =
                visual.Scale.X;

            if (!float.IsFinite(currentScale) ||
                currentScale <= 0)
            {
                currentScale = 1;
            }

            double nextScale;

            if (Math.Abs(
                    targetScale -
                    currentScale) <=
                MagnificationEpsilon)
            {
                nextScale =
                    targetScale;
            }
            else
            {
                nextScale =
                    currentScale +
                    (targetScale -
                     currentScale) *
                    MagnificationSmoothing;

                settled = false;
            }

            visual.Scale =
                new Vector3(
                    (float)nextScale,
                    (float)nextScale,
                    1);

            // Larger icons render over their neighbors instead of being
            // hidden underneath the next StackPanel child.
            Canvas.SetZIndex(
                element,
                Math.Max(
                    0,
                    (int)Math.Round(
                        (nextScale - 1) *
                        1000)));
        }

        if (settled)
            StopMagnificationTimer();
    }

    private double TargetScaleFor(
        FrameworkElement element)
    {
        if (!element.IsLoaded ||
            element.ActualWidth <= 0 ||
            element.ActualHeight <= 0)
        {
            return 1;
        }

        double centerX;

        try
        {
            var transform =
                element.TransformToVisual(
                    icons);

            var center =
                transform.TransformPoint(
                    new global::Windows.Foundation.Point(
                        element.ActualWidth / 2,
                        element.ActualHeight / 2));

            centerX =
                center.X;
        }
        catch (ArgumentException)
        {
            return 1;
        }
        catch (InvalidOperationException)
        {
            return 1;
        }

        var distance =
            Math.Abs(
                centerX -
                magnificationPointerX);

        // Gaussian falloff:
        // center     ~= 1.24
        // 1 neighbor ~= 1.16
        // 2 away     ~= 1.05
        // farther    quickly returns to 1.00
        var influence =
            Math.Exp(
                -(distance * distance) /
                (2 *
                 MagnificationSigma *
                 MagnificationSigma));

        var scale =
            1 +
            (maximumMagnificationScale - 1) *
            influence;

        return scale < 1.003
            ? 1
            : Math.Min(
                maximumMagnificationScale,
                scale);
    }

    private static void ConfigureMagnificationOrigin(
        FrameworkElement element,
        Microsoft.UI.Composition.Visual visual)
    {
        var width =
            element.ActualWidth > 0
                ? element.ActualWidth
                : element.Width;

        var height =
            element.ActualHeight > 0
                ? element.ActualHeight
                : element.Height;

        if (!double.IsFinite(width) ||
            width <= 0)
        {
            width = 40;
        }

        if (!double.IsFinite(height) ||
            height <= 0)
        {
            height = 44;
        }

        // Anchor near the bottom so growth naturally rises upward,
        // like a dock sitting on a shelf.
        visual.CenterPoint =
            new Vector3(
                (float)(width / 2),
                (float)(height * 0.90),
                0);
    }

    private IEnumerable<FrameworkElement> IconElements()
    {
        foreach (var child in icons.Children)
        {
            if (child is not FrameworkElement element)
                continue;

            // Non-app dock content (system tray / clock cluster) deliberately stays
            // compact while the application icons use macOS-style magnification.
            if (string.Equals(element.Tag as string, "NoMagnify", StringComparison.Ordinal))
                continue;

            yield return element;
        }
    }

    private void StartMagnificationTimer()
    {
        if (disposed || magnificationTimerRunning || interactionHeld)
            return;

        magnificationTimerRunning = true;
        magnificationTimer.Start();
    }

    private void StopMagnificationTimer()
    {
        if (!magnificationTimerRunning)
            return;

        magnificationTimerRunning = false;
        magnificationTimer.Stop();
    }

    internal static void Trace(
        string message)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[DockAnimation] {message}");
    }

    private void Add(
        DependencyObject target,
        string property,
        double from,
        EasingMode easingMode,
        params (int Milliseconds, double Value)[] frames)
    {
        var animation =
            new DoubleAnimationUsingKeyFrames
            {
                EnableDependentAnimation = true
            };

        animation.KeyFrames.Add(
            new DiscreteDoubleKeyFrame
            {
                KeyTime =
                    KeyTime.FromTimeSpan(
                        TimeSpan.Zero),
                Value = from
            });

        foreach (var (milliseconds, value) in frames)
        {
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame
                {
                    KeyTime =
                        KeyTime.FromTimeSpan(
                            TimeSpan.FromMilliseconds(
                                milliseconds)),
                    Value = value,
                    EasingFunction =
                        new SineEase
                        {
                            EasingMode =
                                easingMode
                        }
                });
        }

        Storyboard.SetTarget(
            animation,
            target);

        Storyboard.SetTargetProperty(
            animation,
            property);

        active!.Children.Add(
            animation);
    }

    /// <summary>
    /// Stops an in-flight placement/shape transition while retaining the
    /// values currently rendered on screen. This is used only when a context
    /// menu takes ownership of the dock interaction; unlike Stop(), it does
    /// not reset icon magnification or otherwise rebuild the visual state.
    /// </summary>
    public bool FreezeCurrentTransitions()
    {
        var progress = Progress;
        var hadTransition = IsPlacementAnimating || active is not null || indicatorFade is not null;
        var bottom = surface.Margin.Bottom;
        var width = double.IsFinite(surface.Width) ? surface.Width : surface.ActualWidth;
        var height = double.IsFinite(surface.Height) ? surface.Height : surface.ActualHeight;
        var surfaceOpacity = surface.Opacity;
        var indicatorWidth = double.IsFinite(indicator.Width) ? indicator.Width : indicator.ActualWidth;
        var indicatorOpacity = indicator.Opacity;

        placementTimer.Stop();
        IsPlacementAnimating = false;
        var previous = active;
        active = null;
        previous?.Stop();
        completion?.TrySetResult(false);
        completion = null;
        indicatorFade?.Stop();
        indicatorFade = null;

        Progress = progress;
        ApplyProgress();
        surface.Width = width;
        surface.Height = height;
        surface.Opacity = surfaceOpacity;
        indicator.Width = indicatorWidth;
        indicator.Opacity = indicatorOpacity;
        ApplyBottom(bottom);
        return hadTransition;
    }

    public void Stop()
    {
        placementTimer.Stop(); IsPlacementAnimating = false;
        var previous = active;
        active = null;
        previous?.Stop();
        StopIndicatorOpacityAnimation();
        completion?.TrySetResult(false);
        completion = null;

        ResetMagnification(
            immediate: true);

        active = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        disposed = true;
        progressClock.ValueChanged -= ProgressChanged;
        iconTransition.Dispose();
        PlacementChanged = null;
    }
}
