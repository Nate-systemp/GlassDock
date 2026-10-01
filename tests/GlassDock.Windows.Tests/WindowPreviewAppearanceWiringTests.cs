using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class WindowPreviewAppearanceWiringTests
{
    [Fact]
    public void RetainedPreviewUsesTheDockAppearanceAndCorrectBackdropConnectionOrder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var desktop = Path.Combine(directory.FullName, "src", "GlassDock.App", "Desktop");
        var window = File.ReadAllText(Path.Combine(desktop, "WindowPreviewWindow.cs"));
        var controller = File.ReadAllText(Path.Combine(desktop, "WindowPreviewCoordinator.cs"));
        Assert.Contains("UtilityPopupStyle.Apply(glass, backdrop, appearance, mode)", window, StringComparison.Ordinal);
        Assert.Contains("theme.StyleButton(close)", window, StringComparison.Ordinal);
        Assert.True(window.IndexOf("SystemBackdrop = backdrop;", StringComparison.Ordinal) >
            window.IndexOf("placement = new(hwnd, inspection);", StringComparison.Ordinal));
        Assert.Contains("settings.Changed += SettingsChanged", controller, StringComparison.Ordinal);
        Assert.Contains("settings.Changed -= SettingsChanged", controller, StringComparison.Ordinal);
        Assert.Contains("preview?.IsClosing == true", controller, StringComparison.Ordinal);
        Assert.Contains("GetCachedFrame(card.Window)", window, StringComparison.Ordinal);
        var activation = controller[controller.IndexOf("preview.WindowChosen +=", StringComparison.Ordinal)..];
        Assert.True(activation.IndexOf("Hide(immediate: true)", StringComparison.Ordinal) <
            activation.IndexOf("applications.ActivateWindow(selected)", StringComparison.Ordinal));
    }
}
