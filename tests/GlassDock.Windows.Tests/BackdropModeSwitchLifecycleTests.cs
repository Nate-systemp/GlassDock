using Xunit;

namespace GlassDock.Windows.Tests;

/// <summary>
/// Regression guard for live solid/glass mode transitions. The native brush
/// must change in-place; detaching Window.SystemBackdrop while it is loaded
/// may invalidate WinUI's queued default-configuration callback.
/// </summary>
public sealed class BackdropModeSwitchLifecycleTests
{
    [Fact]
    public void Main_dock_never_detaches_xaml_backdrop_during_mode_switch()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var owner = File.ReadAllText(Path.Combine(directory.FullName,
            "src", "GlassDock.App", "Desktop", "DesktopOverlayWindow.cs"));
        var native = File.ReadAllText(Path.Combine(directory.FullName,
            "src", "GlassDock.App", "Rendering", "DesktopGlassBackdrop.cs"));

        Assert.Contains("if (reconnect) desktopBackdrop.RebuildConnectedSurface();",
            owner, StringComparison.Ordinal);
        Assert.DoesNotContain("if (reconnect) SystemBackdrop = null;",
            owner, StringComparison.Ordinal);
        Assert.DoesNotContain("if (reconnect) SystemBackdrop = desktopBackdrop;",
            owner, StringComparison.Ordinal);
        Assert.Contains("private ICompositionSupportsSystemBackdrop? connectedTarget;",
            native, StringComparison.Ordinal);
        Assert.Contains("public void RebuildConnectedSurface()", native,
            StringComparison.Ordinal);
        Assert.Contains("connectedTarget = null;", native,
            StringComparison.Ordinal);
    }
}
