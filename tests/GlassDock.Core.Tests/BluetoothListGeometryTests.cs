using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class BluetoothListGeometryTests
{
    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(3, 1000, 144)]
    [InlineData(10, 1000, 240)]
    [InlineData(10, 120, 120)]
    public void Viewport_fits_devices_without_exceeding_five_rows_or_available_space(int count, double available, double expected) =>
        Assert.Equal(expected, BluetoothListGeometry.ViewportHeight(count, available));

    [Fact]
    public void Edges_fade_continuously_and_reverse_without_history()
    {
        Assert.Equal(1, BluetoothListGeometry.EdgeProgress(0, 48, 0, 240));
        Assert.Equal(.5, BluetoothListGeometry.EdgeProgress(0, 48, 24, 240));
        Assert.Equal(0, BluetoothListGeometry.EdgeProgress(0, 48, 48, 240));
        Assert.Equal(.5, BluetoothListGeometry.EdgeProgress(240, 48, 24, 240));
        Assert.Equal(1, BluetoothListGeometry.EdgeProgress(0, 120, 24, 240));
        Assert.Equal(.5, BluetoothListGeometry.EdgeProgress(0, 48, 24, 240));
    }
}
