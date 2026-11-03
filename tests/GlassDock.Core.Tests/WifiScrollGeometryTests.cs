using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class WifiScrollGeometryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(.1)]
    [InlineData(.5)]
    [InlineData(.9)]
    [InlineData(1)]
    [InlineData(2.4)]
    public void Collapsing_and_entering_rows_share_one_slot_without_gaps_or_overflow(double position)
    {
        var offset = position * WifiScrollGeometry.Pitch;
        var first = (int)Math.Floor(position);
        var outgoing = WifiScrollGeometry.Progress(first, offset, 4);
        var incoming = WifiScrollGeometry.Progress(first + 4, offset, 4);
        Assert.Equal(1, outgoing + incoming, 10);
        for (var index = first + 1; index < first + 4; index++)
            Assert.Equal(1, WifiScrollGeometry.Progress(index, offset, 4));
        var extent = Enumerable.Range(0, 10).Sum(index => WifiScrollGeometry.Progress(index, offset, 4) * WifiScrollGeometry.Pitch);
        Assert.Equal(4 * WifiScrollGeometry.Pitch, extent, 8);
    }

    [Fact]
    public void Direction_reversal_has_no_history_and_slot_boundaries_are_continuous()
    {
        var offset = WifiScrollGeometry.Pitch * .4;
        var before = WifiScrollGeometry.Progress(0, offset, 4);
        Assert.True(WifiScrollGeometry.Progress(0, offset + 1, 4) < before);
        Assert.True(WifiScrollGeometry.Progress(0, offset - 1, 4) > before);
        Assert.Equal(before, WifiScrollGeometry.Progress(0, offset, 4));
        Assert.Equal(0, WifiScrollGeometry.Progress(0, WifiScrollGeometry.Pitch - .01, 4));
        Assert.Equal(1, WifiScrollGeometry.Progress(1, WifiScrollGeometry.Pitch + .01, 4));
        Assert.Equal(0, WifiScrollGeometry.Progress(4, 0, 4));
        Assert.Equal(1, WifiScrollGeometry.Progress(4, WifiScrollGeometry.Pitch, 4));
    }
}
