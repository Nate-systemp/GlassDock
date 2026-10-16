using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class CaptureDrawGateTests
{
    [Fact]
    public void Duplicate_frame_callbacks_schedule_only_one_consumer()
    {
        var gate = new CaptureDrawGate();
        Assert.True(gate.Request(false));
        for (var i = 0; i < 10000; i++) Assert.False(gate.Request(false));
        Assert.False(gate.Begin());
        Assert.True(gate.Request(false));
    }
    [Fact]
    public void Geometry_invalidation_survives_frame_coalescing()
    {
        var gate = new CaptureDrawGate();
        Assert.True(gate.Request(false));
        Assert.False(gate.Request(true));
        Assert.False(gate.Request(false));
        Assert.True(gate.Begin());
        Assert.True(gate.Request(false));
        Assert.False(gate.Begin());
    }
    [Fact]
    public void Invalidation_during_draw_schedules_next_frame()
    {
        var gate = new CaptureDrawGate();
        gate.Request(true);
        Assert.True(gate.Begin());
        Assert.True(gate.Request(true));
        Assert.True(gate.Begin());
    }
    [Fact]
    public void Failed_enqueue_does_not_lose_visual_invalidation()
    {
        var gate = new CaptureDrawGate();
        gate.Request(true);
        gate.EnqueueFailed();
        Assert.True(gate.Request(false));
        Assert.True(gate.Begin());
    }
    [Fact]
    public void Concurrent_producers_are_bounded_and_preserve_dirty_state()
    {
        var gate = new CaptureDrawGate();
        var scheduled = 0;
        Parallel.For(0, 10000, i => { if (gate.Request(i == 42)) Interlocked.Increment(ref scheduled); });
        Assert.Equal(1, scheduled);
        Assert.True(gate.Begin());
    }
}
