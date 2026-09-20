using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DesktopStartupRegressionTests
{
    [Fact]
    public void Desktop_startup_is_queued_before_native_window_configuration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var source = File.ReadAllText(Path.Combine(
            directory.FullName,
            "src",
            "GlassDock.App",
            "Desktop",
            "DesktopOverlayWindow.cs")).ReplaceLineEndings("\n");
        var loadedSubscription = source.IndexOf("root.Loaded += (_, _) => QueueDesktopStartup();", StringComparison.Ordinal);
        var nativeConfiguration = source.IndexOf("windowManager.Configure(inspection);", StringComparison.Ordinal);

        Assert.InRange(loadedSubscription, 0, nativeConfiguration - 1);
        Assert.Contains("QueueDesktopStartup();\n    }", source, StringComparison.Ordinal);
        Assert.Contains("await StartTaskbarTestAsync(whileAppActive: true);", source, StringComparison.Ordinal);
    }
}
