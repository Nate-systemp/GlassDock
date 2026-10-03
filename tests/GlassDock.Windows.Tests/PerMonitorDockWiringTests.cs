using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class PerMonitorDockWiringTests
{
    [Fact]
    public void Coordinator_shares_process_services_and_creates_monitor_local_windows()
    {
        var root = FindRoot();
        string Read(string path) => File.ReadAllText(Path.Combine(root.FullName, path));
        var coordinator = Read("src/GlassDock.App/Desktop/MonitorDockCoordinator.cs");
        var overlay = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.cs");

        Assert.Contains("DokySharedRuntime runtime", coordinator);
        Assert.Contains("runtime.Applications", coordinator);
        Assert.Contains("runtime.Badges", coordinator);
        Assert.Contains("PrimaryWindow.KeyboardService", coordinator);
        Assert.Contains("ownsGlobalServices: false", coordinator);
        Assert.Contains("WindowsMonitorService.GetMonitors()", coordinator);
        Assert.Contains("DockDisplayMode.AllDisplays", coordinator);

        Assert.Contains("snapshotTransform: snapshot => WindowsMonitorService.FilterSnapshotForMonitor", overlay);
        Assert.Contains("if (ownsGlobalServices)", overlay);
        Assert.Contains("if (ownsKeyboard)", overlay);
        Assert.Contains("ShutdownForCoordinatorAsync", overlay);
    }

    [Fact]
    public void Display_setting_exposes_all_displays_without_changing_the_default()
    {
        var root = FindRoot();
        var xaml = File.ReadAllText(Path.Combine(root.FullName,
            "src/GlassDock.App/Desktop/SettingsWindow.xaml"));
        var settings = File.ReadAllText(Path.Combine(root.FullName,
            "src/GlassDock.Core/Settings/GlassDockSettings.cs"));

        Assert.Contains("All displays", xaml);
        Assert.Contains("AllDisplays = 3", settings);
        Assert.Contains("DefaultDockDisplayMode = DockDisplayMode.Primary", settings);
    }

    private static DirectoryInfo FindRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        return root!;
    }
}
