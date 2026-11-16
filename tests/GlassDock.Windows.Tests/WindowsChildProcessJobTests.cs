using System.Diagnostics;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class WindowsChildProcessJobTests
{
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public void Suspended_child_runs_after_assignment_and_preserves_exit_code()
    {
        using var job = new WindowsChildProcessJob();
        using var process = job.Start(PowerShell, "-NoProfile -NonInteractive -Command \"exit 23\"");
        Assert.True(process.WaitForExit(15000));
        Assert.Equal(23, process.ExitCode);
    }

    [Fact]
    public void Disposing_a_job_terminates_only_its_owned_children()
    {
        var firstName = "Local\\Doky.JobTest." + Guid.NewGuid().ToString("N");
        var secondName = "Local\\Doky.JobTest." + Guid.NewGuid().ToString("N");
        using var firstReady = new EventWaitHandle(false, EventResetMode.ManualReset, firstName);
        using var secondReady = new EventWaitHandle(false, EventResetMode.ManualReset, secondName);
        using var firstJob = new WindowsChildProcessJob();
        using var secondJob = new WindowsChildProcessJob();
        using var first = firstJob.Start(PowerShell, WaitArguments(firstName));
        using var second = secondJob.Start(PowerShell, WaitArguments(secondName));
        Assert.True(firstReady.WaitOne(15000));
        Assert.True(secondReady.WaitOne(15000));
        firstJob.Dispose();
        Assert.True(first.WaitForExit(5000));
        Assert.False(second.HasExited);
        secondJob.Dispose();
        Assert.True(second.WaitForExit(5000));
    }

    private static string WaitArguments(string ready) =>
        $"-NoProfile -NonInteractive -Command \"[Threading.EventWaitHandle]::OpenExisting('{ready}').Set(); [Threading.Thread]::Sleep(-1)\"";
}
