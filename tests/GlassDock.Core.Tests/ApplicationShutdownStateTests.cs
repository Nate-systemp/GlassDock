using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class ApplicationShutdownStateTests
{
    [Fact]
    public void Shutdown_is_single_request_and_cancels_late_work()
    {
        var shutdown = new ApplicationShutdownState();

        Assert.False(shutdown.IsRequested);
        Assert.False(shutdown.CancellationToken.IsCancellationRequested);
        Assert.True(shutdown.TryBegin());
        Assert.True(shutdown.IsRequested);
        Assert.True(shutdown.CancellationToken.IsCancellationRequested);
        Assert.False(shutdown.TryBegin());
    }
}
