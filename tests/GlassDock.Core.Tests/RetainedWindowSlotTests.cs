using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class RetainedWindowSlotTests
{
    [Fact]
    public void Repeated_open_reuses_instance_and_stale_close_cannot_release_replacement()
    {
        var slot = new RetainedWindowSlot<object>();
        var creations = 0;
        object Create() { creations++; return new(); }

        var first = slot.GetOrCreate(Create);
        Assert.Same(first, slot.GetOrCreate(Create));
        Assert.Equal(1, creations);
        Assert.True(slot.Release(first));

        var second = slot.GetOrCreate(Create);
        Assert.NotSame(first, second);
        Assert.False(slot.Release(first));
        Assert.Same(second, slot.Current);
        Assert.True(slot.Release(second));
        Assert.Null(slot.Current);
        Assert.Equal(2, creations);
    }

    [Fact]
    public void Shutdown_detaches_current_instance_and_prevents_recreation()
    {
        var slot = new RetainedWindowSlot<object>();
        var current = slot.GetOrCreate(() => new());

        Assert.Same(current, slot.BeginShutdown());
        Assert.True(slot.IsShutdown);
        Assert.Null(slot.Current);
        Assert.Null(slot.BeginShutdown());
        Assert.Throws<InvalidOperationException>(() => slot.GetOrCreate(() => new()));
        Assert.False(slot.Release(current));
    }
}
