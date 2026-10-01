using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockAppMenuWiringTests
{
    [Fact]
    public void AppMenuUsesDockGlassWithPreviewHoldAndStaleStateProtection()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var desktop = Path.Combine(directory.FullName, "src", "GlassDock.App", "Desktop");
        var window = File.ReadAllText(Path.Combine(desktop, "DockAppContextMenuWindow.cs"));
        var coordinator = File.ReadAllText(Path.Combine(desktop, "WindowPreviewCoordinator.cs"));
        Assert.Contains("UtilityPopupStyle.Apply(glass, backdrop, appearance, mode)", window);
        Assert.Contains("theme.StyleButton(button)", window);
        Assert.Contains("icon.SetIcon(item.Application.Icon)", window);
        Assert.True(window.IndexOf("host.Configure()", StringComparison.Ordinal) < window.IndexOf("SystemBackdrop = backdrop", StringComparison.Ordinal));
        Assert.Contains("WindowPreviewLayout.Position", window);
        Assert.Contains("WindowActivationState.Deactivated", window);
        Assert.Contains("VirtualKey.Escape", window);
        Assert.Contains("revision != generation", window);
        Assert.Contains("BeginContextMenu(appMenu)", coordinator);
        Assert.Contains("Hide(immediate: true)", coordinator);
        Assert.Contains("!snapshot.State.Matches(current.Application)", coordinator);
        Assert.Contains("appMenu?.ApplyAppearance(settings.Appearance", coordinator);
        Assert.Contains("appMenu?.Close()", coordinator);
    }
}
