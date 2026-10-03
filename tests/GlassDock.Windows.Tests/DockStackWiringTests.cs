using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockStackWiringTests
{
    [Fact]
    public void PopupUsesSharedServicesAndDismissalPaths()
    {
        var popup = Read("src/GlassDock.App/Desktop/DockStackWindow.cs");
        var wiring = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.Stacks.cs");
        Assert.Contains("WindowActivationState.Deactivated", popup);
        Assert.Contains("VirtualKey.Escape", popup);
        Assert.Contains("UtilityPopupStyle.Apply", popup);
        Assert.Contains("revision != generation", popup);
        Assert.Contains("!stackWindow.IsClosing", wiring);
        Assert.Contains("applicationService.LaunchOrActivate(app)", wiring);
        Assert.Contains("badges.ForApplication", wiring);
        Assert.Contains("\"Rename\"", wiring);
        Assert.Contains("\"Ungroup\"", wiring);
        Assert.DoesNotContain("new WindowsApplicationService", wiring);
        Assert.DoesNotContain("new BadgeCoordinator", wiring);
        Assert.DoesNotContain("new DockPinStore", wiring);
    }

    [Fact]
    public void StackMemberPayloadIsSeparateFromExternalFilesAndClosedStackPreviews()
    {
        var popup = Read("src/GlassDock.App/Desktop/DockStackWindow.cs");
        var desktop = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.cs");
        Assert.Contains("Doky.StackApplication", popup);
        Assert.Contains("e.Data.SetData(DragFormat", popup);
        Assert.Contains("e.DataView.GetDataAsync(DragFormat)", popup);
        Assert.Contains("e.GetDeferral()", popup);
        Assert.DoesNotContain("e.Data.Properties[DragFormat]", popup);
        Assert.Contains("button.StartDragAsync(point)", popup);
        Assert.Contains("UIElement.PointerMovedEvent", popup);
        Assert.Contains("dx * dx + dy * dy < 36", popup);
        Assert.DoesNotContain("SetStorageItems", popup);
        Assert.Contains("StackItemDragOver(e)", desktop);
        Assert.Contains("StackItemDrop(e", desktop);
        Assert.Contains("stackWindow?.ApplyAppearance", desktop);
        Assert.Contains("Stack is null", desktop);
        Assert.Contains("if (holdStackTarget || stackDrag.Mode", desktop);
    }

    [Fact]
    public void MergeCueIsIndependentOfTheTransparentButtonTemplateAndAboveDraggedIcon()
    {
        var wiring = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.Stacks.cs");
        Assert.Contains("content.Children.Add(mergeCue)", wiring);
        Assert.Contains("Canvas.SetZIndex(button, 110)", wiring);
        Assert.Contains("content.Children.Remove(mergeCue)", wiring);
        Assert.Contains("ready ? Visibility.Visible : Visibility.Collapsed", wiring);
        Assert.Contains("holdStackTarget = candidate.Application.Stack is not null", wiring);
    }



    [Fact]
    public void External_storage_drag_stays_separate_from_internal_reorder_and_stack_drag()
    {
        var desktop = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.cs");

        Assert.Contains(
            "private static bool HasStorageItems(DragEventArgs e) =>",
            desktop);
        Assert.Contains(
            "e.DataView.Contains(StandardDataFormats.StorageItems)",
            desktop);
        Assert.DoesNotContain(
            "!reorderDragging && !reorderCommitting && reorderButton is null",
            desktop);

        Assert.Contains(
            "if (paths.All(IsDockPinTarget))",
            desktop);
        Assert.Contains(
            "if (!WindowsApplicationLauncher.CanOpenWith(item.Application))",
            desktop);
        Assert.Contains(
            "e.Handled = false;",
            desktop);
    }

    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, path));
    }
}
