using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class InputHelperProtocolTests
{
    [Fact]
    public void Duplicate_stale_and_invalid_events_cannot_replay_actions()
    {
        long sequence = 0;
        Assert.NotNull(WindowsInputHelperProtocol.ReadEvent("EVENT|1|100|0|HOME", ref sequence, 100));
        Assert.Null(WindowsInputHelperProtocol.ReadEvent("EVENT|1|100|0|HOME", ref sequence, 100));
        Assert.Null(WindowsInputHelperProtocol.ReadEvent("EVENT|2|100|0|HOME", ref sequence, 601));
        Assert.Null(WindowsInputHelperProtocol.ReadEvent("EVENT|3|602|0|BAD", ref sequence, 602));
        Assert.True(WindowsInputHelperProtocol.ReadEvent("EVENT|3|602|4|LAUNCHER", ref sequence, 602)!.Launcher);
        Assert.Equal(3, sequence);
    }
    [Fact]
    public void Session_mutex_rejects_a_second_helper_instance()
    {
        // A synthetic session isolates this test from the real helper.
        using var first = WindowsInputHelperProtocol.AcquireSingleInstance(1000000 + Environment.ProcessId, out var created);
        Assert.True(created);
        try
        {
            using var second = WindowsInputHelperProtocol.AcquireSingleInstance(1000000 + Environment.ProcessId, out var duplicate);
            Assert.False(duplicate);
        }
        finally { first.ReleaseMutex(); }
    }
    [Fact]
    public void Hidden_home_must_not_disable_initial_win_down_suppression()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var keyboardService = File.ReadAllText(Path.Combine(root.FullName, "src", "GlassDock.Windows",
            "Desktop", "WindowsKeyboardService.cs"));
        var nativeHook = File.ReadAllText(Path.Combine(root.FullName, "src", "GlassDock.Windows",
            "Desktop", "WindowsKeyHook.cs"));
        // Home.IsVisible is NOT an acceptable condition: Explorer could see the
        // physical Win-down and activate Windows Start before the bare release.
        Assert.Contains("var captureWinDown = dockShortcutsEnabled;", keyboardService);
        Assert.Contains("hook?.UpdateState(suppressDockToggle, captureWinDown, revision)", keyboardService);
        Assert.Contains("elevatedHelper?.UpdateState(suppressDockToggle, captureWinDown, revision)", keyboardService);
        Assert.Contains("private State state = new(false, true, 0);", nativeHook);
        Assert.Contains("if (captureBareWindowsKey)", nativeHook);
        Assert.Contains("The physical Win-down never reached Windows", nativeHook);
    }

    [Fact]
    public void Elevated_task_must_use_a_protected_helper_instead_of_user_writable_app_output()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var overlay = File.ReadAllText(Path.Combine(root.FullName, "src", "GlassDock.App",
            "Desktop", "DesktopOverlayWindow.cs"));
        var register = File.ReadAllText(Path.Combine(root.FullName, "scripts", "Register-InputHelper.ps1"));
        Assert.Contains("Environment.SpecialFolder.ProgramFiles", overlay);
        Assert.Contains("'Doky\\InputHelper\\GlassDock.InputHelper.exe'", register);
        Assert.Contains("Refusing to elevate a user-writable helper", register);
    }

    [Fact]
    public void Only_shared_hook_installs_and_handoff_removes_fallback_before_grant()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var desktop = Path.Combine(root.FullName, "src", "GlassDock.Windows", "Desktop");
        var app = File.ReadAllText(Path.Combine(desktop, "WindowsKeyboardService.cs"));
        var helper = File.ReadAllText(Path.Combine(desktop, "WindowsInputHelperHost.cs"));
        var shared = File.ReadAllText(Path.Combine(desktop, "WindowsKeyHook.cs"));
        Assert.DoesNotContain("SetWindowsHookEx", app);
        Assert.DoesNotContain("SetWindowsHookEx", helper);
        Assert.Contains("SetWindowsHookExW", shared);
        Assert.DoesNotContain("WriteLine", shared);
        Assert.DoesNotContain("Task.Delay", shared);
        Assert.DoesNotContain("Thread.Sleep", shared);
        Assert.True(app.IndexOf("hook?.Dispose(); hook = null;", StringComparison.Ordinal) <
                    app.IndexOf("elevatedHelper.AllowHelper()", StringComparison.Ordinal));
        Assert.Contains("else StartFallback()", app);
    }
}
