using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class HomeDashboardWiringTests
{
    [Fact]
    public void OneHomeReusesSourcesAndUnsubscribesOnce()
    {
        var home = Read("src/GlassDock.App/Desktop/GlassHomeWindow.cs");
        var overlay = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.cs");
        Assert.Contains("if (home is null)", overlay);
        Assert.Contains("if (ShowHomeOverride is { } showSharedHome)", overlay);
        Assert.Contains("ShowHomeOverride = ownsGlobalServices ? null : () => PrimaryWindow.ShowHome()", Read("src/GlassDock.App/Desktop/MonitorDockCoordinator.cs"));
        Assert.Contains("new DockApplicationsViewModel(applicationService, DispatcherQueue, ownsService: false)", home);
        Assert.DoesNotContain("new WindowsApplicationService", home);
        foreach (var registration in new[] { "settingsSession.Changed", "applications.SnapshotApplied", "applicationIndex.Changed" })
        {
            Assert.Equal(1, Count(home, registration + " +="));
            Assert.Equal(1, Count(home, registration + " -="));
        }
        var handler = home[home.IndexOf("private void SettingsChanged", StringComparison.Ordinal)..home.IndexOf("private void ApplicationsChanged", StringComparison.Ordinal)];
        Assert.Contains("ApplyAppearance()", handler);
        Assert.DoesNotContain("new ", handler);
        Assert.Equal(1, Count(home, "BuildDashboard();"));
        Assert.Contains("AppWindow.Hide()", home);
    }

    [Fact]
    public void FloatingCardsShareOneMaterialAndHaveNoDashboardScrollOrRootShadow()
    {
        var home = Read("src/GlassDock.App/Desktop/GlassHomeWindow.cs");
        var ui = Read("src/GlassDock.App/Desktop/GlassHomeWindow.Dashboard.cs");
        Assert.DoesNotContain("ScrollViewer", home + ui);
        Assert.DoesNotContain("root.Children.Add(surface)", home);
        Assert.Contains("SetSurfaceRegions", home);
        Assert.Contains("UtilityPopupStyle.Apply", ui);
        Assert.Contains("theme.Apply(paletteMode)", ui);
        Assert.Contains("var materialMode = selectedMode;", ui);
        Assert.DoesNotContain("selectedMode.GlassStyle() is null ? DockAppearanceMode.Frosted", ui);
        Assert.Contains("WindowsSystemTheme.IsDark()", ui);
        Assert.Contains("systemTheme.ColorValuesChanged += OnSystemThemeChanged", home);
        Assert.Contains("systemTheme.ColorValuesChanged -= OnSystemThemeChanged", home);
        Assert.Contains("HomeApplications.Select(apps, running: false)", ui);
        Assert.Contains("HomeApplications.Select(apps, running: true)", ui);
        Assert.Contains("No recent items yet", ui);
        Assert.Contains("Nothing playing", ui);
    }

    [Fact]
    public void UnsupportedControlsAreHonestAndPowerRequiresConfirmation()
    {
        var ui = Read("src/GlassDock.App/Desktop/GlassHomeWindow.Controls.cs");
        Assert.Contains("radios.ToggleAsync", ui);
        Assert.Contains("brightness.IsEnabled = bright.Result.HasValue", ui);
        Assert.Contains("systemControls.ReadMasterVolume()", ui);
        Assert.Contains("Night light has no supported direct control", ui);
        Assert.Contains("Focus control is managed by Windows", ui);
        Assert.Contains("await dialog.ShowAsync() != ContentDialogResult.Primary", ui);
        Assert.Contains("if (closed || !IsVisible || refreshingControls) return", ui);
    }

    [Fact]
    public void PlacementUsesSelectedMonitorAndSearchKeepsItsRealIndex()
    {
        var native = Read("src/GlassDock.Windows/Desktop/HomeDesktopEnvironment.cs");
        Assert.Contains("DockDisplayMode.Foreground", native);
        Assert.Contains("DockDisplayMode.Pointer or DockDisplayMode.AllDisplays", native);
        Assert.Contains("GetDpiForMonitor", native);
        Assert.Contains("info.Work", native);
        var search = Read("src/GlassDock.App/Desktop/GlassHomeWindow.Search.cs");
        Assert.Contains("GlassSearch.Find", search);
        Assert.Contains("applicationIndex.Start()", search);
        Assert.Contains("selection.Move", search);
        Assert.Contains("await launcher.LaunchTargetAsync", search);
    }

    private static int Count(string value, string part) => value.Split(part).Length - 1;
    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, path));
    }
}
