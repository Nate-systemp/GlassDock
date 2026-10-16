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

    [Theory]
    [InlineData(0, 1920, 110, 12, 1798)]
    [InlineData(0, 1920, 246, 12, 1662)]
    [InlineData(-1920, 0, 110, 12, -122)]
    [InlineData(-1920, 0, 246, 12, -258)]
    public void Split_utilities_anchor_to_the_far_right_of_the_target_monitor(
        double left, double right, double width, double inset, double expectedX)
    {
        Assert.Equal(expectedX, FloatingUtilityPlacement.RightEdgeX(left, right, width, inset));
    }

    [Fact]
    public void Split_utilities_clock_stays_at_the_same_screen_position_during_expansion()
    {
        var compactX = FloatingUtilityPlacement.RightEdgeX(0, 1920, 110, 12);
        var expandedX = FloatingUtilityPlacement.RightEdgeX(0, 1920, 246, 12);
        Assert.Equal(compactX + 110, expandedX + 246);
    }

    [Fact]
    public void Split_utilities_are_centered_with_a_small_downward_offset()
    {
        Assert.Equal(982, FloatingUtilityPlacement.AlignToDockCenterY(
            dockSurfaceTop: 975, dockSurfaceHeight: 48, itemHeight: 42,
            downwardOffset: 4, screenTop: 0, screenBottom: 1080));
        Assert.Equal(0, FloatingUtilityPlacement.AlignToDockCenterY(
            dockSurfaceTop: -20, dockSurfaceHeight: 48, itemHeight: 42,
            downwardOffset: 4, screenTop: 0, screenBottom: 1080));
    }

    [Fact]
    public void Split_utilities_background_hold_is_six_hundred_milliseconds()
    {
        Assert.Equal(600, UtilitiesHoldGesture.DurationMilliseconds);
    }

    [Theory]
    [InlineData(-.25, 0)]
    [InlineData(0, 0)]
    [InlineData(.25, .15625)]
    [InlineData(.5, .5)]
    [InlineData(.75, .84375)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void Split_hover_uses_smooth_endpoint_safe_easing(double progress, double expected)
    {
        Assert.Equal(expected, FloatingUtilityPlacement.EaseSplitExpansion(progress), 8);
    }

    [Fact]
    public void Split_hover_easing_only_moves_in_one_direction()
    {
        var previous = 0d;
        for (var i = 1; i <= 100; i++)
        {
            var value = FloatingUtilityPlacement.EaseSplitExpansion(i / 100d);
            Assert.InRange(value, previous, 1d);
            previous = value;
        }
    }

    [Theory]
    [InlineData(1870, 1000, 1908, 970, 42, 110, true)]
    [InlineData(1797, 1000, 1908, 970, 42, 110, false)]
    [InlineData(1908, 1000, 1908, 970, 42, 110, false)]
    [InlineData(1870, 1012, 1908, 970, 42, 110, false)]
    [InlineData(-50, 1000, -12, 970, 42, 110, true)]
    public void Split_hover_rearms_only_after_leaving_the_stable_clock_area(
        double x, double y, double right, double top, double height, double compactWidth, bool inside)
    {
        Assert.Equal(inside, FloatingUtilityPlacement.IsInsideCompactHoverArea(
            x, y, right, top, height, compactWidth));
    }

    [Fact]
    public void Split_placement_is_separate_from_existing_top_bar_mode()
    {
        Assert.NotEqual(GlassDock.Core.Settings.UtilitiesPlacement.FloatingButton,
            GlassDock.Core.Settings.UtilitiesPlacement.SplitDock);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);

    [Theory]
    [InlineData(220, 4, 228)]
    [InlineData(100, 4, 108)]
    public void Split_has_one_fixed_native_window_width_for_both_hover_states(
        double expanded, double gutter, double expected)
    {
        Assert.Equal(expected, FloatingUtilityPlacement.SplitHostWidth(expanded, gutter));
    }

    [Theory]
    [InlineData(120, 0, 120, 0)]
    [InlineData(120, .5, 60, 60)]
    [InlineData(120, 1, 0, 120)]
    public void Split_controls_reveal_leftward_without_changing_clock_position(
        double width, double fraction, double left, double visible)
    {
        var clip = FloatingUtilityPlacement.SplitRevealClip(width, fraction);
        Assert.Equal(left, clip.Left);
        Assert.Equal(visible, clip.Width);
        Assert.Equal(width, clip.Left + clip.Width);
    }

    [Theory]
    [InlineData(230, 110, 120)]
    [InlineData(230, 230, 0)]
    public void Split_input_region_only_captures_visible_right_side(int hostWidth, int activeWidth, int expectedLeft)
    {
        Assert.Equal((expectedLeft, activeWidth), FloatingUtilityPlacement.SplitInputRegion(hostWidth, activeWidth));
    }
}
