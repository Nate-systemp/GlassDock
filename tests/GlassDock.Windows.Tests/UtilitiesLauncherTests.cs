using System.Runtime.InteropServices;
using GlassDock.Windows.Desktop;
using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class UtilitiesLauncherTests
{
    [Fact]
    public void Conflicting_shortcut_is_rejected_and_disposal_releases_registration()
    {
        var first = CreateWindowExW(0, "STATIC", "Doky shortcut test A", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        var second = CreateWindowExW(0, "STATIC", "Doky shortcut test B", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        Assert.NotEqual(0, first); Assert.NotEqual(0, second);
        try
        {
            using var a = new WindowsUtilitiesLauncherHost(first);
            using var b = new WindowsUtilitiesLauncherHost(second);
            // Reserve a test-only chord. Never generate input or take over a shortcut.
            Assert.True(a.ConfigureShortcut(true, 0x87)); // Ctrl+Alt+F24
            Assert.False(b.ConfigureShortcut(true, 0x87));
            Assert.True(a.ConfigureShortcut(false, 0));
            Assert.True(b.ConfigureShortcut(true, 0x87));
            b.Dispose();
            Assert.True(a.ConfigureShortcut(true, 0x87));
        }
        finally { DestroyWindow(second); DestroyWindow(first); }
    }

    [Theory]
    [InlineData(1915, 0, 0, 1920, 66, 140, 42, 12, 1768, 12, false)]
    [InlineData(-100, 0, 0, 1920, 66, 140, 42, 12, 12, 12, false)]
    [InlineData(450, 0, 0, 1920, 66, 140, 42, 12, 450, 12, false)]
    [InlineData(450, 0, 0, 1920, 66, 140, 42, 12, 12, 12, true)]
    [InlineData(1670, 0, 0, 1920, 66, 140, 42, 12, 1768, 12, true)]
    [InlineData(-1400, -1920, -1080, 0, -1014, 140, 42, 12, -1400, -1068, false)]
    public void Top_bar_pill_stays_inside_the_reserved_strip(
        double x, double left, double top, double right, double bottom,
        double width, double height, double inset, double expectedX, double expectedY, bool snap)
    {
        var actual = FloatingUtilityPlacement.ClampToTopStrip(
            x, left, top, right, bottom, width, height, inset, snap);
        Assert.Equal(expectedX, actual.X);
        Assert.Equal(expectedY, actual.Y);
    }

    [Fact]
    public void Top_bar_does_not_allow_invalid_or_offscreen_coordinates()
    {
        var result = FloatingUtilityPlacement.ClampToTopStrip(
            double.NaN, 0, 100, 300, 152, 160, 44, 8, false);
        Assert.Equal(132, result.X);
        Assert.Equal(104, result.Y);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
