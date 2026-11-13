using System.Diagnostics;
using System.Numerics;
using GlassDock.App.Rendering;
using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

public sealed partial class DesktopOverlayWindow
{
    private readonly DragLensMotion dragLensMotion = new();
    private readonly Stopwatch dragLensClock = new();
    private readonly Canvas dragLensHost = new() { IsHitTestVisible = false };
    private DockDragLensScene? dragLensScene;
    private Button? dragLensButton;
    private Grid? dragLensContent;
    private ElementTheme dragLensContentTheme;
    private Vector2 dragLensBase, dragLensTarget;
    private float dragLensIconsLeft;
    private float dragLensSize;
    private double dragLensLast;
    private bool dragLensClosing, dragLensAnimating;
    private bool dragLensHoverDeferred;
    private Vector2 dragLensHoverReleasePointer;
    private Vector2? dragLensReleaseSlot;

    private void BeginDragLens(Button button, Vector2 pointerAnchoredCenter)
    {
        AbortDragLens();
        dragLensHoverDeferred = false;
        if (button.Content is not Grid content || (Application.Current as App)?.BasicRendering == true) return;
        try
        {
            var scale = (float)(root.XamlRoot?.RasterizationScale ?? 1);
            dragLensScene = new(icons.Children.OfType<Button>(), button, root, scale);
            dragLensButton = button; dragLensContent = content;
            dragLensContentTheme = content.RequestedTheme;
            content.RequestedTheme = content.ActualTheme;
            var p = button.TransformToVisual(root).TransformPoint(new(button.ActualWidth / 2, button.ActualHeight / 2));
            dragLensBase = new((float)p.X, (float)p.Y);
            dragLensTarget = pointerAnchoredCenter;
            dragLensIconsLeft = (float)icons.TransformToVisual(root).TransformPoint(new(0, 0)).X;
            dragLensSize = (float)Math.Max(48, Appearance.IconSize + 26);
            dragLensMotion.Begin(dragLensTarget, dragLensSize);
            button.RenderTransform = null;
            button.Opacity = 1;
            button.Content = null;
            dragLensHost.Children.Add(liquidGlass.LensPanel);
            dragLensHost.Children.Add(content);
            root.Children.Add(dragLensHost);
            Canvas.SetZIndex(dragLensHost, 1000);
            Canvas.SetLeft(content, dragLensTarget.X - content.Width / 2);
            Canvas.SetTop(content, dragLensTarget.Y - content.Height / 2);
            Canvas.SetZIndex(button, 120);
            liquidGlass.BeginLens(dragLensScene.Draw);
            UpdateLiquidGlass();
            dragLensAnimating = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            dragLensClock.Restart(); dragLensLast = 0;
            CompositionTarget.Rendering += RenderDragLens;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            StartupDiagnostics.Write("Drag lens unavailable; retaining ordinary reorder", error);
            AbortDragLens();
        }
    }

    private void RenderDragLens(object? sender, object args)
    {
        if (dragLensButton is not { } button || dragLensContent is null) return;
        var now = dragLensClock.Elapsed.TotalSeconds;
        var dt = now - dragLensLast; dragLensLast = now;
        Vector2? merge = null;
        var target = dragLensTarget;
        if (!dragLensClosing && stackDrag.Mode == DockDragMode.StackMerge &&
            stackDrag.TargetId is { } id && applicationButtons.TryGetValue(id, out var destination))
        {
            var index = reorderPinnedButtons.IndexOf(destination);
            if (index >= 0 && index < reorderSlotCenters.Length)
            {
                merge = new(dragLensIconsLeft + (float)reorderSlotCenters[index], dragLensBase.Y);
            }
        }
        if (dragLensClosing)
            target = dragLensBase;
        dragLensMotion.Step(dt, target, dragLensSize, merge, dragLensAnimating,
            precisePointer: !dragLensClosing);
        Canvas.SetLeft(dragLensContent, dragLensMotion.Position.X - dragLensContent.Width / 2);
        Canvas.SetTop(dragLensContent, dragLensMotion.Position.Y - dragLensContent.Height / 2);
        liquidGlass.UpdateLens(dragLensMotion.Bounds, dragLensMotion.Opacity, merge.HasValue);
        if (dragLensMotion.Finished)
        {
            AbortDragLens();
            RefreshHoverVisuals();
            SynchronizeItems();
            ScheduleCollapse();
        }
    }

    private void EndDragLens()
    {
        if (dragLensButton is not { } button || dragLensContent is null || dragLensClosing) return;
        dragLensClosing = true;
        button.RenderTransform = null;
        icons.UpdateLayout();
        var p = button.TransformToVisual(root).TransformPoint(new(button.ActualWidth / 2, button.ActualHeight / 2));
        dragLensBase = dragLensReleaseSlot ?? new Vector2((float)p.X, (float)p.Y);
        dragLensReleaseSlot = null;
        Canvas.SetZIndex(button, 120);
        dragLensScene?.Rebase(root);
        dragLensMotion.End(dragLensBase);
        dragLensHoverDeferred = true;
        if (windowManager.TryGetPointerPosition(out var pointerX, out var pointerY))
            dragLensHoverReleasePointer = new((float)pointerX, (float)pointerY);
        System.Diagnostics.Debug.WriteLine(
            $"[Doky drag release] visible={dragLensMotion.Position} slot={dragLensBase} delta={dragLensBase - dragLensMotion.Position}");
    }

    private void AbortDragLens()
    {
        CompositionTarget.Rendering -= RenderDragLens;
        dragLensClock.Stop();
        liquidGlass.EndLens();
        dragLensHost.Children.Clear();
        root.Children.Remove(dragLensHost);
        if (dragLensButton is { } button)
        {
            if (dragLensContent is not null)
            {
                button.Content = dragLensContent;
                dragLensContent.RequestedTheme = dragLensContentTheme;
            }
            Canvas.SetZIndex(button, 0);
        }
        dragLensScene?.Dispose(); dragLensScene = null;
        dragLensButton = null; dragLensContent = null; dragLensClosing = false;
        dragLensReleaseSlot = null;
    }

    private void CaptureDragLensReleaseSlot()
    {
        if (dragLensButton is not { } button) return;
        icons.UpdateLayout();
        var p = button.TransformToVisual(root).TransformPoint(new(button.ActualWidth / 2, button.ActualHeight / 2));
        dragLensReleaseSlot = new Vector2((float)p.X, (float)p.Y);
    }
}
