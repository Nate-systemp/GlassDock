using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DesktopShutdownRegressionTests
{
    [Fact]
    public void Shutdown_hides_surfaces_closes_retained_windows_and_restores_before_main_close()
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
        var shutdownStart = source.IndexOf("private async Task ShutdownAsync", StringComparison.Ordinal);
        Assert.True(shutdownStart >= 0);
        var shutdown = source[shutdownStart..];

        var hideMain = shutdown.IndexOf("AppWindow.Hide();", StringComparison.Ordinal);
        var closeSettings = shutdown.IndexOf("settingsWindow.BeginShutdown()?.Close();", StringComparison.Ordinal);
        var restoreTaskbar = shutdown.IndexOf("TaskbarRecovery.RestoreNow();", StringComparison.Ordinal);
        var disposeWatchdog = shutdown.IndexOf("await session.DisposeAsync();", StringComparison.Ordinal);
        var closeMain = shutdown.IndexOf("if (closeMainWindow) Close();", StringComparison.Ordinal);
        var completeApplication = shutdown.IndexOf("shutdownCompleted();", StringComparison.Ordinal);

        Assert.All(
            new[] { hideMain, closeSettings, restoreTaskbar, disposeWatchdog, closeMain, completeApplication },
            index => Assert.True(index >= 0));
        Assert.True(hideMain < closeSettings);
        Assert.True(closeSettings < restoreTaskbar);
        Assert.True(restoreTaskbar < disposeWatchdog);
        Assert.True(disposeWatchdog < closeMain);
        Assert.True(closeMain < completeApplication);
        Assert.Contains("MenuItem(menu, \"Exit GlassDock\", RequestShutdown);", source, StringComparison.Ordinal);
        Assert.Contains("private void OnClosed(object sender, WindowEventArgs e) => BeginShutdown(closeMainWindow: false);", source, StringComparison.Ordinal);
    }
}
