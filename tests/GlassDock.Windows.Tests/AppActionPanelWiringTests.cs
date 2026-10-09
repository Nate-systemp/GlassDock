using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class AppActionPanelWiringTests
{
    [Fact]
    public void RootAndStackUseOnePanelAndExistingServices()
    {
        var coordinator = Read("src/GlassDock.App/Desktop/WindowPreviewCoordinator.cs");
        var stack = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.Stacks.cs");
        var grid = Read("src/GlassDock.App/Desktop/DockStackWindow.cs");
        Assert.Contains("ShowAppActions(item, Anchor(button))", coordinator);
        Assert.Contains("previews.ShowAppActions", stack);
        Assert.Contains("applicationService.ExtractStack(stackId, app.Id)", stack);
        Assert.Contains("AppActionsRequested?.Invoke(app, value.Id, anchor)", grid);
        Assert.Contains("MenuItem(menu, \"Unstack all apps\"", stack);
        Assert.Contains("applications.ActivateWindow(window) || await restoreElevated(window)", coordinator);
        Assert.Contains("applications.CloseWindow(window)", coordinator);
        Assert.Contains("applications.SetPinned(item, !state.IsPinned)", coordinator);
        Assert.Contains("Hide(immediate: true)", coordinator);
        Assert.Contains("new PointerEventHandler(DockPointerPressed), true", coordinator);
        Assert.Contains("dockRoot.RemoveHandler(UIElement.PointerPressedEvent", coordinator);
        Assert.Contains("host.ActivateForUserInput()", Read("src/GlassDock.App/Desktop/DockAppContextMenuWindow.cs"));
        Assert.DoesNotContain("new WindowsApplicationService", coordinator);
    }

    [Fact]
    public void FilePickerUsesExistingOpenWithAndIsBoundedByOneActiveOperation()
    {
        var coordinator = Read("src/GlassDock.App/Desktop/WindowPreviewCoordinator.cs");
        var overlay = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.cs");
        var launcher = Read("src/GlassDock.Windows/Applications/WindowsApplicationLauncher.cs");
        Assert.Contains("if (disposed || filePickerActive) return", coordinator);
        Assert.Contains("GetWindowIdFromWindow(dock)", coordinator);
        Assert.Contains("await openFiles(application, selected.Select(file => file.Path).ToArray())", coordinator);
        Assert.Contains("dropLauncher.OpenWithAsync", overlay);
        Assert.Contains("completion.SetResult(OpenWith(application, paths))", launcher);
        Assert.Contains("dropLauncher.OpenWith(item.Application, paths)", overlay);
        Assert.DoesNotContain("AutomaticDestinations", coordinator);
    }

    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, path));
    }
}
