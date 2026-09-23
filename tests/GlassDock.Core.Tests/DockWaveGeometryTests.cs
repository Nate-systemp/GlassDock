using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockWaveGeometryTests
{
    [Theory]
    [InlineData(134)]
    [InlineData(175)]
    [InlineData(300)]
    [InlineData(425)]
    [InlineData(466)]
    public void Wave_segments_join_with_continuous_tangents(double center)
    {
        var outline = DockWaveGeometry.Create(100, 80, 400, 68, 34, center, 68, 18, 1);
        var start = outline.Start;
        for (var i = 0; i < outline.Segments.Count; i++)
        {
            var segment = outline.Segments[i];
            var next = outline.Segments[(i + 1) % outline.Segments.Count];
            var incoming = segment.IsLine ? Difference(segment.End, start) : Difference(segment.End, segment.Control2);
            var outgoing = next.IsLine ? Difference(next.End, segment.End) : Difference(next.Control1, segment.End);
            Assert.True(double.IsFinite(segment.End.X) && double.IsFinite(segment.End.Y));
            Assert.InRange(Math.Abs(incoming.X * outgoing.Y - incoming.Y * outgoing.X), 0, 1e-7);
            Assert.True(incoming.X * outgoing.X + incoming.Y * outgoing.Y >= 0);
            start = segment.End;
        }
        Assert.Equal(outline.Start, outline.Segments[^1].End);
        Assert.InRange(outline.Segments.Count, 8, 18);
    }

    [Fact]
    public void Wave_corner_does_not_jump_when_crossing_old_merge_threshold()
    {
        // The old path switched entire corner branches near this position.
        for (var x = 150d; x < 210; x += .1)
        {
            var a = DockWaveGeometry.Create(100, 80, 400, 68, 34, x, 68, 18, 1);
            var b = DockWaveGeometry.Create(100, 80, 400, 68, 34, x + .1, 68, 18, 1);
            Assert.InRange(Math.Abs(a.Segments[0].End.Y - b.Segments[0].End.Y), 0, .1);
            Assert.InRange(Math.Abs(a.Segments[0].Control2.Y - b.Segments[0].Control2.Y), 0, .15);
        }
    }

    [Fact]
    public void Motion_is_independent_of_frame_rate()
    {
        double Follow(int frames)
        {
            var value = 0d;
            for (var i = 0; i < frames; i++) value = DockWaveGeometry.Follow(value, 100, 1d / frames, .0385);
            return value;
        }
        Assert.Equal(Follow(60), Follow(144), 10);
        Assert.Equal(Follow(60), Follow(30), 10);
    }

    private static DockWaveGeometry.Point Difference(DockWaveGeometry.Point a, DockWaveGeometry.Point b) => new(a.X - b.X, a.Y - b.Y);
}
