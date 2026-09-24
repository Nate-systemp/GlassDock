using System.Diagnostics;
using System.Numerics;
using GlassDock.App.Rendering;
using GlassDock.Core.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

/// <summary>One rendering clock transforms the stable layout and native glass together.</summary>
internal sealed class UtilityPopupPresentation
{
    private readonly Window window;
    private readonly FrameworkElement root;
    private readonly DesktopGlassBackdrop backdrop;
    private readonly Stopwatch clock = new();
    private RectInt32 finalBounds;
    private RectInt32 hostBounds;
    private double rasterScale = 1, sourceX, sourceY, progress, from;
    private bool positioned, requested, started, hiding, finished, activated;
    private bool openingQueued;
    private int revision;
    public event EventHandler? Dismissed;
    public event EventHandler? Hidden;
    public bool IsVisible => requested && !finished;
    private PopupCompositionTrack? track;
    private CompositionScopedBatch? batch;
    private readonly PopupCompositionFrame[] frames = new PopupCompositionFrame[33];
    public int InputHeightPixels => finalBounds.Height;

    public UtilityPopupPresentation(Window window, FrameworkElement root, DesktopGlassBackdrop backdrop,
        Func<bool>? utilityOwnsPointer = null)
    {
        this.window = window;
        this.root = root;
        this.backdrop = backdrop;
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        ElementCompositionPreview.GetElementVisual(root).Opacity = 0;
        root.IsHitTestVisible = false;
        backdrop.SetPresentation(0, 0, 0, 0, 0, 0, 0);
        root.Loaded += (_, _) => TryStart();
        window.Activated += (_, args) =>
        {
            if (finished) return;
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // Pointer-down activates the dock before Button.Click (pointer-up).
                // That interaction belongs to the utility toggle, not outside-dismiss.
                if (activated && started)
                {
                    var observedRevision = revision;
                    var utilityInteraction = utilityOwnsPointer?.Invoke() == true;
                    if (utilityInteraction) return;
                    root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () =>
                        {
                            if (!finished && UtilityPopupRequests.ShouldDismissOnDeactivation(
                                utilityInteraction, observedRevision, revision)) Dismiss();
                        });
                }
            }
            else { activated = true; TryStart(); }
        };
        window.AppWindow.Closing += (_, e) =>
        {
            if (finished) return;
            e.Cancel = true;
            Dismiss();
        };
        window.Closed += (_, _) => { finished = true; StopTransition(); track?.Dispose(); track = null; window.SystemBackdrop = null; };
    }

    public void SetTargetWindowGeometry(double screenX, double screenY, double scale)
    {
        if (finished) return;
        rasterScale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        finalBounds = new(window.AppWindow.Position.X, window.AppWindow.Position.Y,
            window.AppWindow.Size.Width, window.AppWindow.Size.Height);
        sourceX = (screenX - finalBounds.X) / rasterScale;
        sourceY = (screenY - finalBounds.Y) / rasterScale;
        // Reserve transparent travel space once. HWND dimensions never animate.
        hostBounds = finalBounds;
        hostBounds.Height = Math.Max(finalBounds.Height,
            (int)Math.Ceiling(screenY - finalBounds.Y + UtilityPopupStyle.Gutter * rasterScale));
        window.AppWindow.MoveAndResize(hostBounds);
        root.Width = finalBounds.Width / rasterScale;
        root.Height = finalBounds.Height / rasterScale;
        positioned = true;
        UpdateBackdropBounds();
        ApplyFrame();
    }

    public void UpdateBackdropBounds()
    {
        if (!positioned) return;
        backdrop.SetBounds(hostBounds.Width / rasterScale, hostBounds.Height / rasterScale,
            Math.Max(0, root.Width - UtilityPopupStyle.Gutter * 2),
            Math.Max(0, root.Height - UtilityPopupStyle.Gutter * 2),
            (hostBounds.Height - finalBounds.Height) / rasterScale + UtilityPopupStyle.Gutter,
            rasterScale, 1);
    }

    public void Present()
    {
        if (finished) return;
        requested = true;
        if (started) { if (hiding) Start(false); else revision++; }
        else TryStart();
    }
    private void TryStart()
    {
        if (!requested || !positioned || root.XamlRoot is null || started || finished || hiding || openingQueued) return;
        openingQueued = true;
        // Wait until synchronous tray/hardware initialization has completed.
        root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            openingQueued = false;
            if (started || finished || hiding || !requested) return;
            started = true;
            Start(false);
        });
    }
    public void Dismiss()
    {
        if (finished || hiding) return;
        Dismissed?.Invoke(this, EventArgs.Empty);
        RetargetClosed();
    }
    public void RetargetClosed()
    {
        if (finished || hiding) return;
        if (!started) { HideImmediately(); return; }
        Start(true);
    }
    public void HideImmediately()
    {
        if (finished || !requested) return;
        requested = false;
        started = hiding = activated = false;
        revision++;
        StopTransition();
        progress = 0;
        ApplyFrame();
        window.AppWindow.Hide();
        Hidden?.Invoke(this, EventArgs.Empty);
    }
    public void CloseImmediately()
    {
        if (finished) return;
        finished = true;
        revision++;
        StopTransition();
        window.Close();
    }
    private void Start(bool close)
    {
        // Sampling is only needed on input, never on a managed frame callback.
        if (clock.IsRunning) progress = PopupMorph.Progress(clock.Elapsed.TotalSeconds, hiding, from);
        StopTransition();
        hiding = close;
        revision++;
        from = progress;
        root.IsHitTestVisible = false;
        var duration = TimeSpan.FromSeconds(close ? .21 : .25);
        for (var i = 0; i < frames.Length; i++)
        {
            var p = PopupMorph.Progress(duration.TotalSeconds * i / (frames.Length - 1), close, from);
            frames[i] = new(PopupMorph.Funnel(p, root.Width, root.Height, sourceX, sourceY, UtilityPopupStyle.Gutter),
                (float)PopupMorph.Frame(p).Opacity);
        }
        var visual = ElementCompositionPreview.GetElementVisual(root);
        track ??= new PopupCompositionTrack(visual);
        track.Bind();
        batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        track.Start(frames, duration);
        backdrop.AnimatePresentation(frames, duration);
        batch.Completed += Complete;
        clock.Restart();
        batch.End();
    }
    private void StopTransition()
    {
        clock.Stop();
        if (batch is not null)
        {
            batch.Completed -= Complete;
            batch.Dispose();
            batch = null;
        }
        track?.Stop();
        backdrop.StopPresentationAnimation();
    }
    private void Complete(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (finished || !ReferenceEquals(sender, batch)) return;
        progress = hiding ? 0 : 1;
        StopTransition();
        ApplyFrame();
        if (hiding) HideImmediately();
        else root.IsHitTestVisible = true;
    }
    private void ApplyFrame()
    {
        if (!positioned) return;
        var frame = PopupMorph.Frame(progress);
        var visual = ElementCompositionPreview.GetElementVisual(root);
        // Composition coordinates of the XAML visual are DIPs. The backdrop
        // converts the same values to its physical coverage-mask coordinates.
        var matrix = PopupMorph.Funnel(progress, root.Width, root.Height,
            sourceX, sourceY, UtilityPopupStyle.Gutter);
        visual.TransformMatrix = matrix;
        visual.Opacity = (float)frame.Opacity;
        backdrop.SetPresentationTransform(matrix, frame.Opacity);
    }
}
