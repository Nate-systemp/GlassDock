using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class CoreDockWindowCommandTests
{
    [Fact]
    public void Rejected_window_commands_do_not_launch_or_change_running_model()
    {
        using var service = new WindowsApplicationService();
        var identity = new ApplicationIdentity("invalid", "", "");
        var window = new ApplicationWindow(identity, "Closed", 0, int.MaxValue, 0, false, null);
        Assert.False(service.CanInteractWithWindow(window));
        Assert.False(service.ActivateWindow(window));
        Assert.False(service.MinimizeWindow(window));
        Assert.False(service.CloseWindow(window));
        Assert.False(service.IsForeground(window));
        Assert.False(service.IsMinimized(window));
    }
}
