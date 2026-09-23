using System.Diagnostics;
using GlassDock.App.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

/// <summary>
/// Animates the utility popup as one physical window toward/from the exact dock
/// control that owns it. The HWND bounds, XAML content and desktop-glass mask
/// change together so the content cannot disappear before the glass rectangle.
/// </summary>
internal sealed class UtilityPopupPresentation
{
    private const double OpenDurationMilliseconds = 320;
    private const double CloseDurationMilliseconds = 280;
    private const double FirstFrameHoldMilliseconds = 24;

    // The collapsed window remains large enough for DWM/WinUI to keep presenting
    // reliable frames, but small enough to read as terminating at the utility icon.
    private const double CollapsedWidthDips = 34;
    private const double CollapsedHeightDips = 18;

    private readonly Window window;
    private readonly FrameworkElement root;
    private readonly DesktopGlassBackdrop backdrop;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private readonly CompositeTransform transform = new();
    private readonly Stopwatch clock = new();

    private RectInt32 finalBounds;
    private bool hasFinalBounds;
    private double rasterScale = 1;
    private double anchorScreenX;
    private double anchorScreenY;

    private double progress;
    private double from;
    private double durationMilliseconds = OpenDurationMilliseconds;
    private bool hiding;
    private bool finished;
    private bool hasActivated;
    private bool presentRequested;
    private bool openingStarted;

    public UtilityPopupPresentation(
        Window window,
        FrameworkElement root,
        DesktopGlassBackdrop backdrop)
    {
        this.window = window;
        this.root = root;
        this.backdrop = backdrop;

        // Keep the popup's content arranged at its final size. During the transition
        // we scale that stable layout to the current HWND size. This prevents grids,
        // labels, sliders and calendar cells from reflowing/disappearing before the
        // outer glass window has finished its motion.
        root.RenderTransform = transform;
        root.RenderTransformOrigin = new global::Windows.Foundation.Point(0, 0);
        root.Opacity = 0;
        root.IsHitTestVisible = false;
        backdrop.SetPresentation(1, 1, 0, 0, 0, 0, 0);

        timer = root.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(8);
        timer.Tick += (_, _) => Tick();

        root.Loaded += (_, _) => TryStartOpening();

        window.Activated += (_, args) =>
        {
            if (finished)
                return;

            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                if (hasActivated && openingStarted && !hiding)
                    Dismiss();
                return;
            }

            hasActivated = true;
            TryStartOpening();
        };

        window.AppWindow.Closing += (_, e) =>
        {
            if (finished)
                return;

            e.Cancel = true;
            if (!openingStarted)
            {
                CloseImmediately();
                return;
            }

            if (!hiding)
                Start(close: true);
        };

        window.Closed += (_, _) =>
        {
            finished = true;
            timer.Stop();
        };
    }

    /// <summary>
    /// Captures the popup's final on-screen rectangle after UtilityPopupStyle has
    /// positioned it, then parks the not-yet-presented HWND at the source icon.
    /// Coordinates are physical screen pixels; scale converts the stable XAML layout
    /// back to DIPs.
    /// </summary>
    public void SetTargetWindowGeometry(
        double sourceScreenX,
        double sourceScreenY,
        double scale)
    {
        if (finished)
            return;

        rasterScale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        anchorScreenX = double.IsFinite(sourceScreenX)
            ? sourceScreenX
            : window.AppWindow.Position.X + window.AppWindow.Size.Width / 2d;
        anchorScreenY = double.IsFinite(sourceScreenY)
            ? sourceScreenY
            : window.AppWindow.Position.Y + window.AppWindow.Size.Height;

        finalBounds = new RectInt32(
            window.AppWindow.Position.X,
            window.AppWindow.Position.Y,
            Math.Max(1, window.AppWindow.Size.Width),
            Math.Max(1, window.AppWindow.Size.Height));
        hasFinalBounds = true;

        // Freeze layout at the final dimensions. The actual HWND is what shrinks;
        // the XAML tree scales as one unit instead of independently reflowing.
        root.Width = finalBounds.Width / rasterScale;
        root.Height = finalBounds.Height / rasterScale;

        if (!openingStarted)
        {
            progress = 0;
            ApplyFrame(0);
            return;
        }

        // Repositioning while visible (monitor/DPI changes) keeps the current
        // transition progress instead of flashing the window at full size.
        ApplyFrame(progress);
    }

    public void Present()
    {
        if (finished || hiding || openingStarted)
            return;

        presentRequested = true;
        TryStartOpening();
    }

    public void CloseImmediately()
    {
        if (finished)
            return;

        finished = true;
        presentRequested = false;
        timer.Stop();
        window.Close();
    }

    public void Dismiss()
    {
        if (finished || hiding)
            return;

        if (!openingStarted)
        {
            CloseImmediately();
            return;
        }

        Start(close: true);
    }

    private void TryStartOpening()
    {
        if (!presentRequested || finished || hiding || openingStarted)
            return;

        if (!hasFinalBounds || root.XamlRoot is null)
            return;

        presentRequested = false;
        openingStarted = true;
        Start(close: false);
    }

    private void Start(bool close)
    {
        if (finished || !hasFinalBounds)
            return;

        hiding = close;
        root.IsHitTestVisible = false;
        from = progress;
        durationMilliseconds = close
            ? CloseDurationMilliseconds
            : OpenDurationMilliseconds;

        clock.Restart();
        ApplyFrame(progress);
        timer.Start();
    }

    private void Tick()
    {
        if (finished)
        {
            timer.Stop();
            return;
        }

        var elapsed = clock.Elapsed.TotalMilliseconds;

        // Guarantee at least one collapsed frame after activation. Otherwise DWM can
        // coalesce the initial MoveAndResize and the first expansion into one frame.
        if (!hiding && elapsed < FirstFrameHoldMilliseconds)
        {
            ApplyFrame(0);
            return;
        }

        var effectiveElapsed = hiding
            ? elapsed
            : Math.Max(0, elapsed - FirstFrameHoldMilliseconds);
        var t = Math.Clamp(
            effectiveElapsed / Math.Max(1, durationMilliseconds),
            0,
            1);

        // SmootherStep has zero velocity at both ends. That matters here because the
        // physical HWND edges are moving; abrupt endpoint velocity is much easier to
        // notice than it is on an ordinary opacity/scale animation.
        var eased = SmootherStep(t);
        var target = hiding ? 0d : 1d;
        progress = from + (target - from) * eased;
        ApplyFrame(progress);

        if (t < 1)
            return;

        timer.Stop();
        progress = target;
        ApplyFrame(progress);

        if (hiding)
        {
            CloseImmediately();
            return;
        }

        root.IsHitTestVisible = true;
    }

    private void ApplyFrame(double value)
    {
        if (!hasFinalBounds)
            return;

        var p = Math.Clamp(value, 0, 1);
        var bounds = InterpolateWindowBounds(p);

        if (window.AppWindow.Position.X != bounds.X ||
            window.AppWindow.Position.Y != bounds.Y ||
            window.AppWindow.Size.Width != bounds.Width ||
            window.AppWindow.Size.Height != bounds.Height)
        {
            window.AppWindow.MoveAndResize(bounds);
        }

        // Stable final-size layout -> scale exactly to the physical HWND currently
        // on screen. The content, glass-surface chrome and controls therefore remain
        // locked to the same rectangle for every frame.
        var scaleX = bounds.Width / (double)Math.Max(1, finalBounds.Width);
        var scaleY = bounds.Height / (double)Math.Max(1, finalBounds.Height);
        transform.CenterX = 0;
        transform.CenterY = 0;
        transform.ScaleX = scaleX;
        transform.ScaleY = scaleY;
        transform.TranslateX = 0;
        transform.TranslateY = 0;

        // Do not fade content early. It remains fully visible for almost the entire
        // collapse and only fades when the whole HWND is already icon-sized.
        var opacity = SmoothRamp(p, 0.06, 0.18);
        root.Opacity = opacity;

        // SystemBackdrop is window-level, so the physical HWND provides the main
        // shrink. Rebuild its glass mask for the CURRENT window rectangle so the
        // visible glass body and refractive inner edge shrink with the same bounds,
        // instead of leaving a full-size rectangle behind the scaled content.
        var currentWidthDips = bounds.Width / rasterScale;
        var currentHeightDips = bounds.Height / rasterScale;
        backdrop.SetPresentation(1, 1, 0, 0, 0, 0, opacity);
        backdrop.SetBounds(
            currentWidthDips,
            currentHeightDips,
            Math.Max(0, currentWidthDips - UtilityPopupStyle.Gutter * 2),
            Math.Max(0, currentHeightDips - UtilityPopupStyle.Gutter * 2),
            UtilityPopupStyle.Gutter,
            rasterScale,
            opacity);
    }

    private RectInt32 InterpolateWindowBounds(double progressValue)
    {
        var collapsedWidth = Math.Max(1, (int)Math.Round(CollapsedWidthDips * rasterScale));
        var collapsedHeight = Math.Max(1, (int)Math.Round(CollapsedHeightDips * rasterScale));

        var collapsedLeft = anchorScreenX - collapsedWidth / 2d;
        var collapsedTop = anchorScreenY - collapsedHeight / 2d;
        var collapsedRight = collapsedLeft + collapsedWidth;
        var collapsedBottom = collapsedTop + collapsedHeight;

        var finalLeft = (double)finalBounds.X;
        var finalTop = (double)finalBounds.Y;
        var finalRight = finalBounds.X + (double)finalBounds.Width;
        var finalBottom = finalBounds.Y + (double)finalBounds.Height;

        // Interpolating all four edges independently makes the complete window
        // converge on the exact clicked source point. It is not just a scale inside
        // a stationary rectangle; the outer glass rectangle itself physically moves.
        var left = Lerp(collapsedLeft, finalLeft, progressValue);
        var top = Lerp(collapsedTop, finalTop, progressValue);
        var right = Lerp(collapsedRight, finalRight, progressValue);
        var bottom = Lerp(collapsedBottom, finalBottom, progressValue);

        var x = (int)Math.Round(left);
        var y = (int)Math.Round(top);
        var width = Math.Max(1, (int)Math.Round(right - left));
        var height = Math.Max(1, (int)Math.Round(bottom - top));
        return new RectInt32(x, y, width, height);
    }

    private static double SmootherStep(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    private static double SmoothRamp(double value, double fromValue, double toValue)
    {
        if (toValue <= fromValue)
            return value >= toValue ? 1 : 0;

        var t = Math.Clamp((value - fromValue) / (toValue - fromValue), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Lerp(double fromValue, double toValue, double amount) =>
        fromValue + (toValue - fromValue) * amount;
}
