using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockRelocationGuardTests
{
    [Fact]
    public void Reentrant_monitor_notifications_do_not_overlap_and_repeated_moves_work()
    {
        var guard = new DockRelocationGuard();
        var moves = 0;
        for (var i = 0; i < 60; i++)
            Assert.True(guard.Run(() =>
            {
                moves++;
                Assert.False(guard.Run(() => throw new Exception("nested relocation"), _ => false, _ => { }));
            }, _ => false, _ => { }));
        Assert.Equal(60, moves);
    }

    [Theory]
    [InlineData("popup disposal")]
    [InlineData("monitor removed")]
    [InlineData("capture lost")]
    public void Recoverable_failure_is_reported_and_next_move_is_allowed(string stage)
    {
        var guard = new DockRelocationGuard();
        var error = new InvalidOperationException(stage);
        Exception? reported = null;
        Assert.False(guard.Run(() => throw error, e => e is InvalidOperationException, e => reported = e));
        Assert.Same(error, reported);
        Assert.True(guard.Run(() => { }, _ => false, _ => { }));
    }

    [Fact]
    public void Programming_errors_are_not_swallowed_and_shutdown_blocks_pending_work()
    {
        var guard = new DockRelocationGuard();
        Assert.Throws<NullReferenceException>(() => guard.Run(() => throw new NullReferenceException(),
            e => e is InvalidOperationException, _ => { }));
        guard.Stop();
        Assert.False(guard.Run(() => throw new Exception("closed window touched"), _ => false, _ => { }));
    }
}
