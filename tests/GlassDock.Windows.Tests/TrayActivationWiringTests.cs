using Xunit;

namespace GlassDock.Windows.Tests;

// Boundary checks complement executable routing/identity/persistence tests.
// The old assertions required Shift+F10 and an unavailable-native-menu message;
// both behaviors were explicitly removed by the supported-only requirement.
public sealed class TrayActivationWiringTests
{
    [Fact]
    public void Discovery_cannot_show_move_enable_or_synthesize_input_to_explorer()
    {
        var source = Source("src/GlassDock.Windows/Desktop/WindowsTrayAccessibility.cs");
        Assert.DoesNotContain("SendInput", source);
        Assert.DoesNotContain("SetWindowPos", source);
        Assert.DoesNotContain("EnableWindow", source);
        Assert.DoesNotContain("OverflowScanSession", source);
        Assert.DoesNotContain("TryInvokeOverflowChevron", source);
        Assert.Contains("IsCurrentExplorer(item)", source);
        Assert.Contains("Release(accessible)", source);
    }

    [Fact]
    public void Right_click_is_labeled_doky_management_and_never_routes_to_native_or_settings()
    {
        var source = Source("src/GlassDock.App/Desktop/SystemTrayWindow.cs");
        var start = source.IndexOf("button.ContextRequested +=", StringComparison.Ordinal);
        var end = source.IndexOf("button.DragStarting +=", start, StringComparison.Ordinal);
        var context = source[start..end];
        Assert.Contains("Doky tray controls", context);
        Assert.Contains("Move earlier", context);
        Assert.Contains("Move later", context);
        Assert.DoesNotContain("OpenTraySettings", context);
        Assert.DoesNotContain("TryShowNativeContextMenu", source);
        Assert.DoesNotContain("native tray menu unavailable", source);
        Assert.DoesNotContain("OpenApplication(", context);
    }

    [Fact]
    public void Explicit_shortcuts_are_labeled_and_hidden_popups_stop_refreshing()
    {
        var source = Source("src/GlassDock.App/Desktop/SystemTrayWindow.cs");
        Assert.Contains("app shortcut, not a live tray icon", source);
        Assert.Contains("Live action", source);
        Assert.Contains("presentation.Hidden += (_, _) => refreshTimer.Stop()", source);
        Assert.Contains("refreshTimer.Stop();", source);
        Assert.Contains("draggedKey is null", source);
        Assert.Contains("activationPending", source);
    }

    [Fact]
    public void App_open_does_not_launch_another_background_instance()
    {
        var source = Source("src/GlassDock.Windows/Desktop/WindowsTrayAccessibility.cs");
        var start = source.IndexOf("private static bool OpenOrActivate", StringComparison.Ordinal);
        var end = source.IndexOf("public static Task<bool> InvokeAsync", start, StringComparison.Ordinal);
        var action = source[start..end];
        Assert.Contains("return false;", action[..action.IndexOf("Process.Start(", StringComparison.Ordinal)]);
        Assert.Contains("WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!", action);
    }

    private static string Source(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, path));
    }
}
