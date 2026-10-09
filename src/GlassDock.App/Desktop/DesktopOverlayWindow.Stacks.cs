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
    private readonly DockDragIntent dragIntent = new();
    private bool suppressDragContext;
    private DockDragBounds[] reorderHitBounds = [];
    private DockPointerTarget dragPointerTarget = new(DockPointerMode.Outside, -1);
    private Button? mergeHighlight;
    private Border? mergeCue;
    private TextBlock? mergeReadyMark;
    private bool holdStackTarget;
    private DockStackWindow? stackWindow;
    private readonly Dictionary<Button, (Microsoft.UI.Xaml.Media.Animation.Storyboard Story, double Target)> reorderShifts = new();

    private void AnimateReorderShift(Button button, double target)
    {
        if (reorderShifts.TryGetValue(button, out var previous) && previous.Target == target) return;
        var transform = button.RenderTransform as TranslateTransform ?? new TranslateTransform();
        var current = transform.X;
        previous.Story?.Stop();
        transform.X = current;
        button.RenderTransform = transform;
        if (!new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        { transform.X = target; reorderShifts.Remove(button); return; }
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var motion = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = current, To = target, Duration = TimeSpan.FromMilliseconds(130), EnableDependentAnimation = true,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(motion, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(motion, "X");
        story.Children.Add(motion); reorderShifts[button] = (story, target); story.Begin();
    }

    private void StopReorderShifts()
    {
        foreach (var (button, shift) in reorderShifts)
        {
            if (button.RenderTransform is not TranslateTransform transform) { shift.Story.Stop(); continue; }
            var current = transform.X; shift.Story.Stop(); transform.X = current;
        }
        reorderShifts.Clear();
    }

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
            stackWindow.AppActionsRequested += (app, stackId, anchor) =>
                previews.ShowAppActions(new DockApplicationItem(app), anchor,
                    () => applicationService.ExtractStack(stackId, app.Id));
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
        menu.Opening += (_, _) => previews.BeginContextMenu(menu);
        menu.Closed += (_, _) => previews.EndContextMenu(menu);
        MenuItem(menu, "Open", () => ToggleStack(item));
        MenuItem(menu, "Rename", () => ToggleStack(item, true));
        MenuItem(menu, "Unstack all apps", () =>
        {
            stackWindow?.Hide();
            SetStatus(applicationService.ExtractStack(item.Id)
                ? "All apps returned to the dock."
                : "Could not unstack apps. Please try again.");
        });
        menu.ShowAt(button);
    }

    private void UpdateStackCandidate(double pointerX, double pointerY)
    {
        if (!reorderDragging || reorderCandidate is null) return;
        var previous = dragPointerTarget.Mode == DockPointerMode.Stack ? dragPointerTarget.Index : -1;
        dragPointerTarget = dragIntent.Resolve(pointerX, pointerY, reorderHitBounds,
            reorderSourceIndex, index => reorderCandidate.IsPinned && reorderCandidate.Application.Stack is null &&
                index < VisibleDockApplications.Count && VisibleDockApplications[index].IsPinned &&
                VisibleDockApplications[index].Application.StackApps.Count < DockStack.MaximumApps, previous);
        holdStackTarget = dragPointerTarget.Mode == DockPointerMode.Stack;
        reorderTargetIndex = dragPointerTarget.Mode == DockPointerMode.Reorder ? dragPointerTarget.Index : reorderSourceIndex;
        if (stackDrag.Mode == DockDragMode.None) stackDrag.Begin();
        var target = holdStackTarget ? VisibleDockApplications[dragPointerTarget.Index].Id : null;
        // Preview is immediate; persistence happens only on pointer release.
        stackDrag.PreviewTarget(target);
        if (target is null) ClearMergeHighlight(); else ShowMergeReady();
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
    private void ClearStackDrag() { dragIntent.Reset(); stackDrag.Reset(); holdStackTarget = false; reorderHitBounds = []; dragPointerTarget = new(DockPointerMode.Outside, -1); ClearMergeHighlight(); }
    private bool FinishStackMerge(Button button, DockApplicationItem item, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(icons).Position;
        UpdateStackCandidate(point.X, point.Y);
        if (!dragIntent.StackMode || stackDrag.Mode != DockDragMode.StackMerge || stackDrag.TargetId is not { } target) return false;
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


