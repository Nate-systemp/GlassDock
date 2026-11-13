using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class InputHelperProtocolTests
{
    [Fact]
    public void Volume_events_share_sequence_and_expire_without_accepting_arbitrary_keys()
    {
        long sequence = 0;
        Assert.Equal((uint)0xAF, WindowsInputHelperProtocol.ReadVolumeEvent("VOLUME|1|1000|175", ref sequence, 1000));
        Assert.Null(WindowsInputHelperProtocol.ReadVolumeEvent("VOLUME|1|1000|175", ref sequence, 1000));
        Assert.Null(WindowsInputHelperProtocol.ReadVolumeEvent("VOLUME|2|1000|65", ref sequence, 1000));
        Assert.Null(WindowsInputHelperProtocol.ReadVolumeEvent("VOLUME|2|1000|174|extra", ref sequence, 1000));
        Assert.Null(WindowsInputHelperProtocol.ReadVolumeEvent("VOLUME|2|1000|174", ref sequence, 1501));
        Assert.Null(WindowsInputHelperProtocol.ReadVolumeEvent("VOLUME|3|1600|173", ref sequence, 1599));
        Assert.Equal((uint)0xAD, WindowsInputHelperProtocol.ReadVolumeEvent("VOLUME|4|1600|173", ref sequence, 1600));
        Assert.NotNull(WindowsInputHelperProtocol.ReadEvent("EVENT|5|1600|0|HOME", ref sequence, 1600));
    }

    [Fact]
    public void Five_hundred_helper_events_preserve_order_and_reject_duplicate_delivery()
    {
        long sequence = 0;
        for (var i = 1; i <= 500; i++)
        {
            var command = $"EVENT|{i}|{1000 + i}|4|HOME";
            Assert.NotNull(WindowsInputHelperProtocol.ReadEvent(command, ref sequence, 1000 + i));
            Assert.Null(WindowsInputHelperProtocol.ReadEvent(command, ref sequence, 1000 + i));
            Assert.Equal(i, sequence);
        }
    }

    [Fact]
    public void Snipping_events_share_sequence_with_regular_win_signals()
    {
        long sequence = 0;
        var snip = WindowsInputHelperProtocol.ReadSnippingEvent("SNIP|1|1000|7", ref sequence, 1001);
        Assert.NotNull(snip);
        Assert.Equal((uint)7, snip.Revision);
        Assert.Equal(1, sequence);
        Assert.Null(WindowsInputHelperProtocol.ReadSnippingEvent("SNIP|1|1000|7", ref sequence, 1001));
        Assert.NotNull(WindowsInputHelperProtocol.ReadEvent("EVENT|2|1002|7|HOME", ref sequence, 1002));
        Assert.Equal(2, sequence);
        Assert.Null(WindowsInputHelperProtocol.ReadSnippingEvent("SNIP|3|1000|7", ref sequence, 1501));
        Assert.Null(WindowsInputHelperProtocol.ReadSnippingEvent("SNIP|3|1000|7|extra", ref sequence, 1002));
        Assert.Null(WindowsInputHelperProtocol.ReadSnippingEvent("SNIP|3|1000|not-a-revision", ref sequence, 1002));
        Assert.Null(WindowsInputHelperProtocol.ReadSnippingEvent("SNIP|-1|1000|7", ref sequence, 1002));
        Assert.NotNull(WindowsInputHelperProtocol.ReadSnippingEvent("SNIP|4|1004|7", ref sequence, 1004));
        Assert.NotNull(WindowsInputHelperProtocol.ReadSnippingEnd("SNIPEND|5|1005|7", ref sequence, 1005));
        Assert.Null(WindowsInputHelperProtocol.ReadSnippingEnd("SNIPEND|5|1005|7", ref sequence, 1005));
    }

    [Fact]
    public void RestoreRequestsRequireIdentityAndRejectExpiredOrMalformedCommands()
    {
        var id = Guid.NewGuid().ToString("N");
        var command = $"RESTORE|{id}|123|456|789|1000";
        var request = WindowsInputHelperProtocol.ReadRestore(command, 1500);
        Assert.NotNull(request);
        Assert.Equal(id, request.Id);
        Assert.Equal(123, request.Window.Handle);
        Assert.Equal(456, request.Window.ProcessId);
        Assert.Equal(789, request.Window.ProcessStartTicks);
        Assert.Null(WindowsInputHelperProtocol.ReadRestore(command, 2001));
        Assert.Null(WindowsInputHelperProtocol.ReadRestore(command, 999));
        Assert.Null(WindowsInputHelperProtocol.ReadRestore($"RESTORE|{id}|0|456|789|1000", 1500));
        Assert.Null(WindowsInputHelperProtocol.ReadRestore($"RESTORE|{id}|123|0|789|1000", 1500));
        Assert.Null(WindowsInputHelperProtocol.ReadRestore($"RESTORE|{id}|123|456|0|1000", 1500));
        Assert.Null(WindowsInputHelperProtocol.ReadRestore("RESTORE|invalid|123|456|789|1000", 1500));
        Assert.Null(WindowsInputHelperProtocol.ReadRestore("STATE|0|1|0", 1500));
    }

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
    public void Recovery_command_carries_the_latest_input_state_before_rearming()
    {
        var id = Guid.NewGuid().ToString("N");
        var command = WindowsInputHelperProtocol.CreateRecoveryCommand(id, "STATE|1|0|47");
        var recovery = WindowsInputHelperProtocol.ReadRecovery(command);
        Assert.NotNull(recovery);
        Assert.Equal(id, recovery.Id);
        Assert.Equal("STATE|1|0|47", recovery.State);
        Assert.Null(WindowsInputHelperProtocol.ReadRecovery($"RECOVER|{id}|1|2|47"));
        Assert.Null(WindowsInputHelperProtocol.ReadRecovery("RECOVER|invalid"));
        Assert.NotNull(WindowsInputHelperProtocol.ReadRecovery($"RECOVER|{id}"));
    }
    [Fact]
    public void Session_mutex_rejects_a_second_helper_instance()
    {
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
        Assert.Contains("var captureWinDown = dockShortcutsEnabled;", keyboardService);
        Assert.Contains("hook?.UpdateState(suppressDockToggle, captureWinDown, revision)", keyboardService);
        Assert.Contains("elevatedHelper?.UpdateState(suppressDockToggle, captureWinDown, revision)", keyboardService);
        Assert.Contains("private State state = new(false, true, 0);", nativeHook);
        Assert.Contains("if (captureBareWindowsKey)", nativeHook);
        Assert.Matches(@"if \(captureBareWindowsKey\)\s+\{\s+capturedWindowsDown = true;" +
            @"\s+capturedWindowsKey = keyCode;\s+return 1;", nativeHook);
        var capturedRelease = nativeHook.IndexOf("if (capturedWindowsDown &&", StringComparison.Ordinal);
        Assert.True(capturedRelease >= 0);
        var nextBranch = nativeHook.IndexOf("if (wasLauncherShortcut)", capturedRelease, StringComparison.Ordinal);
        Assert.True(nextBranch > capturedRelease);
        var release = nativeHook[capturedRelease..nextBranch];
        Assert.Contains("capturedWindowsKey == keyCode", release);
        Assert.Contains("capturedWindowsDown = false;", release);
        Assert.Contains("signal(false, pressRevision);", release);
        Assert.Contains("return 1;", release);
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
