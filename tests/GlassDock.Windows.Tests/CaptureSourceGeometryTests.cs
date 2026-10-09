using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class CaptureSourceGeometryTests
{
    private static readonly DesktopCaptureSource.Placement Original = new(1, 100, 800, 900, 144, 1920, 1080);

    [Theory]
    [InlineData(1)] [InlineData(1.25)] [InlineData(1.5)] [InlineData(1.75)] [InlineData(2)]
    public void Window_resize_or_dpi_change_retains_monitor_capture(double scale)
    {
        var resized = Original with { X = 0, Y = 500, Width = (int)(900 * scale), Height = (int)(144 * scale) };
        Assert.True(Original.SameCaptureSource(resized));
    }

    [Fact]
    public void Resolution_orientation_and_monitor_changes_require_new_frame_pool()
    {
        Assert.False(Original.SameCaptureSource(Original with { MonitorWidth = 2560, MonitorHeight = 1440 }));
        Assert.False(Original.SameCaptureSource(Original with { MonitorWidth = 1080, MonitorHeight = 1920 }));
        Assert.False(Original.SameCaptureSource(Original with { Monitor = 2 }));
        Assert.True(Original.SameCaptureSource(Original));
    }
}
