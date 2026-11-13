using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class TaskbarResumeWiringTests
{
    [Fact]
    public void Heartbeat_timeout_waits_for_a_new_ui_hide_request_while_emergency_recovery_still_exits()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var watchdog = File.ReadAllText(Path.Combine(directory.FullName, "src", "GlassDock.Watchdog", "Program.cs"));
        var session = File.ReadAllText(Path.Combine(directory.FullName, "src", "GlassDock.Windows", "Desktop", "TaskbarDevelopmentSession.cs"));

        Assert.Contains("if (!deadline.CanRenewAfterTimeout) break;", watchdog);
        Assert.Contains("if (!waitingForHeartbeat)", watchdog);
        Assert.Contains("if (!restored) restored = controller.EmergencyRestore();", watchdog);
        Assert.Contains("Console.WriteLine(\"WAITING: heartbeat expired.\");", watchdog);
        Assert.Contains("if (hiddenAt is not null && !waitingForHeartbeat)", watchdog);
        Assert.Contains("while (!parent.HasExited && !emergency.WaitOne(0))", watchdog);
        Assert.Contains("if (command is null or \"RESTORE\") break;", watchdog);
        Assert.Contains("Interlocked.Exchange(ref renewalRequested, 0) == 1 ? \"HIDE\" : \"PING\"", session);
        Assert.Contains("if (line == \"WAITING: heartbeat expired.\") Interlocked.Exchange(ref renewalRequested, 1);", session);
    }
}
