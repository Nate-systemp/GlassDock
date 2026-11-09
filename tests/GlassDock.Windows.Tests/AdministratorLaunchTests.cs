using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class AdministratorLaunchTests
{
    [Fact]
    public async Task Shell_wait_stays_on_background_sta_without_blocking_caller()
    {
        var entered = new TaskCompletionSource<(int Thread, ApartmentState Apartment)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = Environment.CurrentManagedThreadId;
        var launch = WindowsApplicationLauncher.RunOnStaAsync(() =>
        {
            entered.SetResult((Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
            release.Task.GetAwaiter().GetResult();
            return true;
        });
        try
        {
            var worker = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(caller, worker.Thread);
            Assert.Equal(ApartmentState.STA, worker.Apartment);
            Assert.False(launch.IsCompleted);
        }
        finally { release.TrySetResult(); }

        Assert.True(await launch.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("shell:AppsFolder\\Vendor.App!App")]
    [InlineData("https://example.com/app.exe")]
    [InlineData("C:\\Windows\\System32\\notepad.exe.lnk")]
    public async Task Administrator_launch_rejects_non_executable_targets(string? target)
    {
        var identity = new ApplicationIdentity(null, target);
        var application = new DockApplication(identity.Key, identity, "App", target, false, [], null);

        Assert.False(await new WindowsApplicationLauncher().RunAsAdministratorAsync(application, 0));
    }
}
