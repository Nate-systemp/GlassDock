using System.Diagnostics;
using GlassDock.App.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

// One clock drives content and the native backdrop mask, including closing.
internal sealed class UtilityPopupPresentation
{
    private readonly Window window;
    private readonly FrameworkElement root;
    private readonly DesktopGlassBackdrop backdrop;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private readonly CompositeTransform transform = new();
    private readonly Stopwatch clock = new();
    private double progress;
    private double from;
    private bool hiding;
    private bool finished;
    public double AnchorX { get; set; }

    public UtilityPopupPresentation(Window window, FrameworkElement root, DesktopGlassBackdrop backdrop)
    {
        this.window = window;
        this.root = root;
        this.backdrop = backdrop;
        root.RenderTransform = transform;
        root.Opacity = 0;
        backdrop.SetPresentation(1, 0, 0, 0, 0);
        timer = root.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) => Tick();
        root.Loaded += (_, _) => root.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => { if (!finished && !hiding) Start(false); });
        window.AppWindow.Closing += (_, e) =>
        {
            if (finished) return;
            e.Cancel = true;
            if (!hiding) Start(true);
        };
        window.Closed += (_, _) => { finished = true; timer.Stop(); };
    }

    public void CloseImmediately()
    {
        finished = true;
        timer.Stop();
        window.Close();
    }

    public void Dismiss()
    {
        if (!finished && !hiding) Start(true);
    }

    private void Start(bool close)
    {
        hiding = close;
        root.IsHitTestVisible = !close;
        from = progress;
        // Like the dock transformation, this explicitly requested transition stays
        // visible when Windows disables optional window animations.
        // Initial tray/hardware discovery must not consume its animation duration.
        clock.Reset();
        Apply(progress);
        timer.Start();
    }

    private void Tick()
    {
        if (!clock.IsRunning) clock.Start();
        var t = Math.Clamp(clock.Elapsed.TotalMilliseconds / 190, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        Apply(from + ((hiding ? 0 : 1) - from) * eased);
        if (t < 1) return;
        timer.Stop();
        if (hiding) CloseImmediately();
    }

    private void Apply(double value)
    {
        progress = value;
        var scale = .965 + .035 * value;
        var offset = 6 * (1 - value);
        var origin = Math.Clamp(AnchorX, 16, Math.Max(16, root.ActualWidth - 16));
        transform.CenterX = origin;
        transform.CenterY = root.ActualHeight - 16;
        transform.ScaleX = transform.ScaleY = scale;
        transform.TranslateY = offset;
        root.Opacity = value;
        backdrop.SetPresentation(scale, offset, origin, root.ActualHeight - 16, value);
    }
}
