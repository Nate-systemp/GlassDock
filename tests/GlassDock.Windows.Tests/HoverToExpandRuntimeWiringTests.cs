using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class HoverToExpandRuntimeWiringTests
{
    [Fact]
    public void Hover_only_expansion_is_persisted_and_blocks_non_hover_open_paths()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var root = directory.FullName;
        var settings = File.ReadAllText(Path.Combine(root, "src", "GlassDock.Core", "Settings", "GlassDockSettings.cs"))
            .ReplaceLineEndings("\n");
        var session = File.ReadAllText(Path.Combine(root, "src", "GlassDock.Core", "Settings", "GlassDockSettingsSession.cs"))
            .ReplaceLineEndings("\n");
        var xaml = File.ReadAllText(Path.Combine(root, "src", "GlassDock.App", "Desktop", "SettingsWindow.xaml"))
            .ReplaceLineEndings("\n");
        var shell = File.ReadAllText(Path.Combine(root, "src", "GlassDock.App", "Desktop", "SettingsWindow.Shell.cs"))
            .ReplaceLineEndings("\n");
        var window = File.ReadAllText(Path.Combine(root, "src", "GlassDock.App", "Desktop", "DesktopOverlayWindow.cs"))
            .ReplaceLineEndings("\n");

        Assert.Contains("public bool HoverToExpandOnly { get; init; }", settings, StringComparison.Ordinal);
        Assert.Contains("bool? hoverToExpandOnly = null", session, StringComparison.Ordinal);
        Assert.Contains("HoverToExpandOnly = hoverToExpandOnly ?? Current.HoverToExpandOnly", session, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HoverToExpandOnlyToggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HoverToExpandOnlyToggle.Toggled +=", shell, StringComparison.Ordinal);
        Assert.Contains("if (settingsSession.Current.HoverToExpandOnly)", window, StringComparison.Ordinal);
        Assert.Contains("if (settingsSession.Current.HoverToExpandOnly &&", window, StringComparison.Ordinal);
    }
}
