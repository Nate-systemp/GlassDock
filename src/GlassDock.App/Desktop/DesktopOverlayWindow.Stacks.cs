using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace GlassDock.App.Desktop;

public sealed partial class DesktopOverlayWindow
{
    private readonly DockStackDrag stackDrag = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? stackDwellTimer;
    private Button? mergeHighlight;
    private Border? mergeCue;
    private TextBlock? mergeReadyMark;
    private bool holdStackTarget;
    private DockStackWindow? stackWindow;

    private BadgeDisplayState StackBadge(DockApplication app) => app.Stack is null ? badges.ForApplication(app.Identity) :
        app.StackApps.Any(child => badges.ForApplication(child.Identity).IsVisible) ? BadgeDisplayState.Activity("stack") : BadgeDisplayState.None;

    private void ToggleStack(DockApplicationItem item, bool rename = false)
    {
        if (item.Application.Stack is null) return;
        if (stackWindow?.IsOpen == true && !stackWindow.IsClosing && stackWindow.StackId == item.Id && !rename) { stackWindow.Hide(); return; }
        previews.Hide(); utilityRequests.Reset(); utilityTransitionPending = false;
        CloseQuickSettings(); CloseSystemTray(); CloseCalendar();
        if (stackWindow is null)
        {
            stackWindow = new(WinRT.Interop.WindowNative.GetWindowHandle(this), app => applicationService.LaunchOrActivate(app),
                badges.ForApplication, (id, name) => applicationService.RenameStack(id, name),
                (id, order) => applicationService.ReorderStack(id, order), StackSourceOwnsPointer);
            stackWindow.Hidden += (_, _) => { if (!closing) { RefreshHoverVisuals(); ScheduleCollapse(); } };
        }
        stackWindow.ApplyAppearance(Appearance, settingsSession.Current.DockAppearanceMode);
        var button = applicationButtons[item.Id];
        var point = button.TransformToVisual(root).TransformPoint(new(button.ActualWidth / 2, 0));
        var top = ExpandedContentTop;
        stackWindow.Show(item.Application, point.X, top);
        if (rename) stackWindow.Rename();
        collapseDelay?.Cancel(); CancelPillHide();
    }

    private bool StackSourceOwnsPointer()
    {
        if (stackWindow?.StackId is not { } id || !applicationButtons.TryGetValue(id, out var button)) return false;
        if (!windowManager.TryGetPointerPosition(out var x, out var y)) return false;
        var bounds = button.TransformToVisual(root).TransformBounds(new global::Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
        return bounds.Contains(new global::Windows.Foundation.Point(x, y));
    }

    private void RepositionStackPopup()
    {
        if (stackWindow?.IsOpen != true || stackWindow.StackId is not { } id ||
            !applicationButtons.TryGetValue(id, out var button)) return;
        var point = button.TransformToVisual(root).TransformPoint(new(button.ActualWidth / 2, 0));
        var top = ExpandedContentTop;
        stackWindow.Position(point.X, top);
    }
    private void StackContext(Button button, DockApplicationItem item)
    {
        var menu = new MenuFlyout();
        MenuItem(menu, "Open", () => ToggleStack(item));
        MenuItem(menu, "Rename", () => ToggleStack(item, true));
        MenuItem(menu, "Ungroup", () => { stackWindow?.Hide(); applicationService.ExtractStack(item.Id); });
        menu.ShowAt(button);
    }

    private void UpdateStackCandidate(double pointerX, double pointerY)
    {
        holdStackTarget = false;
        if (!reorderDragging || reorderCandidate is null) return;
        if (stackDrag.Mode == DockDragMode.None) stackDrag.Begin();
        string? target = null;
        if (reorderCandidate.IsPinned && reorderCandidate.Application.Stack is null && pointerY >= 0 && pointerY <= icons.ActualHeight)
        {
            var index = ResolveReorderTargetIndex(pointerX);
            if (index >= 0 && index < VisibleDockApplications.Count && index != reorderSourceIndex)
            {
                var candidate = VisibleDockApplications[index];
                if (candidate.IsPinned && candidate.Application.StackApps.Count < DockStack.MaximumApps)
                {
                    // Freeze an existing stack throughout its slot so it cannot slide away
                    // before the pointer reaches the narrower centered merge region.
                    holdStackTarget = candidate.Application.Stack is not null;
                    if (Math.Abs(pointerX - reorderSlotCenters[index]) <= Appearance.ButtonWidth * 0.30) target = candidate.Id;
                }
            }
        }
        stackDrag.Hover(target, Environment.TickCount64);
        if (target is null) { stackDwellTimer?.Stop(); ClearMergeHighlight(); return; }
        stackDwellTimer ??= CreateStackTimer();
        if (!stackDwellTimer.IsRunning) stackDwellTimer.Start();
        ShowMergeReady();
    }
    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateStackTimer()
    {
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(60);
        timer.Tick += (_, _) =>
        {
            if (!reorderDragging || stackDrag.TargetId is null) { timer.Stop(); return; }
            stackDrag.Hover(stackDrag.TargetId, Environment.TickCount64); ShowMergeReady();
            if (stackDrag.Mode == DockDragMode.StackMerge) timer.Stop();
        };
        return timer;
    }
    private void ShowMergeReady()
    {
        if (stackDrag.TargetId is null) return;
        var button = applicationButtons.GetValueOrDefault(stackDrag.TargetId);
        if (button?.Content is not Grid content) return;
        var ready = stackDrag.Mode == DockDragMode.StackMerge;
        if (!ReferenceEquals(mergeHighlight, button))
        {
            ClearMergeHighlight(); mergeHighlight = button;
            mergeReadyMark ??= new TextBlock { Text = "+", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, -5, -2, 0) };
            mergeCue ??= new Border { CornerRadius = new(10), BorderThickness = new(1.5),
                Margin = new(-2), IsHitTestVisible = false, Child = mergeReadyMark };
            mergeReadyMark.Foreground = DockForegroundBrush();
            mergeCue.BorderBrush = DockForegroundBrush();
            // Button pointer-over resources intentionally suppress its template border.
            // Keep the cue inside the tile but above the dragged source (Z=100).
            content.Children.Add(mergeCue);
            Canvas.SetZIndex(button, 110);
            SetStatus("Hold to create a stack");
        }
        mergeCue!.Opacity = ready ? 1 : 0.45;
        mergeReadyMark!.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        if (ready) SetStatus("Release to add to stack");
    }
    private void ClearMergeHighlight()
    {
        if (mergeHighlight is not null)
        {
            if (mergeHighlight.Content is Grid content && mergeCue is not null) content.Children.Remove(mergeCue);
            Canvas.SetZIndex(mergeHighlight, 0);
            mergeHighlight = null;
        }
    }
    private void ClearStackDrag() { stackDwellTimer?.Stop(); stackDrag.Reset(); holdStackTarget = false; ClearMergeHighlight(); }
    private bool FinishStackMerge(Button button, DockApplicationItem item, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(icons).Position;
        UpdateStackCandidate(point.X, point.Y);
        if (stackDrag.Mode != DockDragMode.StackMerge || stackDrag.TargetId is not { } target) return false;
        var saved = applicationService.MergeStack(item.Id, target);
        suppressClickUntil[item.Id] = DateTime.UtcNow.AddMilliseconds(750);
        CancelReorder(); button.ReleasePointerCapture(e.Pointer); e.Handled = true;
        SetStatus(saved ? "Stack saved." : "Could not create stack.");
        return true;
    }

    private bool StackItemDragOver(DragEventArgs e)
    {
        if (!DockStackWindow.HasPayload(e)) return false;
        if (closing || shutdown.IsRequested) { e.Handled = true; e.AcceptedOperation = DataPackageOperation.None; return true; }
        if (!externalDragActive) DockStackWindow.TraceDrag("Dock accepted member drag");
        e.Handled = true; e.AcceptedOperation = DataPackageOperation.Move; externalDragActive = true;
        e.DragUIOverride.Caption = "Move out of stack";
        e.DragUIOverride.IsCaptionVisible = true;
        collapseDelay?.Cancel(); CancelPillHide(); previews.Hide();
        if (state.State is DockState.Idle or DockState.Hovering) _ = ExpandDockAsync();
        return true;
    }
    private async Task<bool> StackItemDrop(DragEventArgs e, string? beforeId = null)
    {
        if (!DockStackWindow.HasPayload(e)) return false;
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            var data = await DockStackWindow.PayloadAsync(e);
            var saved = data is not null && !closing && !shutdown.IsRequested && applicationService.ExtractStack(data.StackId, data.AppId, beforeId);
            e.AcceptedOperation = saved ? DataPackageOperation.Move : DataPackageOperation.None;
            DockStackWindow.TraceDrag("Dock drop: saved=" + saved);
            stackWindow?.Hide();
        }
        finally { SetExternalDropVisual(false); deferral.Complete(); }
        return true;
    }
}
