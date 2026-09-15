using System.Numerics;
using GlassDock.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using global::Windows.UI.ViewManagement;

namespace GlassDock.App.Desktop;

internal sealed class DockAnimationController
{
    private const double MaxMagnificationScale = 1.24;
    private const double MagnificationSigma = 52;
    private const double MagnificationEpsilon = 0.0015;
    private const double MagnificationSmoothing = 0.34;

    private readonly GlassSurface surface;
    private readonly Panel icons;
    private readonly FrameworkElement indicator;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer magnificationTimer;

    private Storyboard? active;
    private TaskCompletionSource<bool>? completion;

    private bool magnificationRequested;
    private bool magnificationTimerRunning;
    private double magnificationPointerX;

    public DockAnimationController(
        GlassSurface surface,
        Panel icons,
        FrameworkElement indicator)
    {
        this.surface = surface;
        this.icons = icons;
        this.indicator = indicator;

        magnificationTimer =
            icons.DispatcherQueue.CreateTimer();

        magnificationTimer.Interval =
            TimeSpan.FromMilliseconds(16);

        magnificationTimer.Tick +=
            (_, _) => TickMagnification();
    }

    public Task<bool> AnimateAsync(
        bool expanded,
        double targetWidth = 560)
    {
        var started =
            System.Diagnostics.Stopwatch.StartNew();

        Trace(
            $"AnimateAsync({expanded}, targetWidth={targetWidth}), previousPending={completion is { Task.IsCompleted: false }}");

        var width = surface.ActualWidth;
        var height = surface.ActualHeight;
        var opacity = icons.Opacity;
        var surfaceOpacity = surface.Opacity;
        var indicatorOpacity = indicator.Opacity;
        var indicatorWidth = indicator.ActualWidth;

        active?.Stop();
        completion?.TrySetResult(false);

        surface.Width = width;
        surface.Height = height;
        icons.Opacity = opacity;
        surface.Opacity = surfaceOpacity;
        indicator.Opacity = indicatorOpacity;
        indicator.Width = indicatorWidth;

        var animationsEnabled =
            new UISettings().AnimationsEnabled;

        Trace(
            $"AnimationsEnabled={animationsEnabled}");

        active = new Storyboard();

        // Keep this explicitly requested transformation visible even when Windows
        // disables optional animations. Other UI animations retain their OS policy.
        if (expanded)
        {
            Add(
                indicator,
                "Opacity",
                indicatorOpacity,
                (100, indicatorOpacity),
                (220, 0));

            Add(
                surface,
                "Width",
                width,
                (320, targetWidth));

            Add(
                surface,
                "Height",
                height,
                (320, 68));

            Add(
                surface,
                "Opacity",
                surfaceOpacity,
                (90, surfaceOpacity),
                (320, 1));

            Add(
                icons,
                "Opacity",
                opacity,
                (220, opacity),
                (380, 1));
        }
        else
        {
            ResetMagnification();

            Add(
                surface,
                "Width",
                width,
                (300, 120));

            Add(
                surface,
                "Height",
                height,
                (280, 5));

            Add(
                icons,
                "Opacity",
                opacity,
                (60, opacity),
                (220, 0));

            Add(
                surface,
                "Opacity",
                surfaceOpacity,
                (180, surfaceOpacity),
                (300, 0));

            Add(
                indicator,
                "Width",
                indicatorWidth,
                (180, indicatorWidth),
                (300, 120));

            Add(
                indicator,
                "Opacity",
                indicatorOpacity,
                (200, indicatorOpacity),
                (300, 1));
        }

        var pending =
            new TaskCompletionSource<bool>();

        completion = pending;

        active.Completed +=
            (_, _) =>
            {
                Trace(
                    $"Completed({expanded}) elapsed={started.ElapsedMilliseconds}ms width={surface.ActualWidth} height={surface.ActualHeight} icons={icons.Opacity}");

                pending.TrySetResult(true);
            };

        Trace(
            $"Begin({expanded})");

        active.Begin();

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
            (MaxMagnificationScale - 1) *
            influence;

        return scale < 1.003
            ? 1
            : Math.Min(
                MaxMagnificationScale,
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
            if (child is FrameworkElement element)
                yield return element;
        }
    }

    private void StartMagnificationTimer()
    {
        if (magnificationTimerRunning)
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
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
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

    public void Stop()
    {
        active?.Stop();
        completion?.TrySetResult(false);

        ResetMagnification(
            immediate: true);

        active = null;
    }
}
