using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockSizingTests
{
    [Theory]
    [InlineData(.7)] [InlineData(.8)] [InlineData(1)]
    public void Floating_and_attached_share_content_but_reserve_their_actual_footprint(double scale)
    {
        var attached = new DockSizing(scale, 28, PinnedDockPlacement.BottomAttached, 14);
        var floating = attached with { Placement = PinnedDockPlacement.Floating };
        Assert.Equal(68 * scale, attached.Height, 8);
        Assert.Equal(attached.Height, attached.SurfaceHeight + attached.SurfaceBottom, 8);
        Assert.Equal(14 * scale, floating.SurfaceBottom, 8);
        Assert.Equal(floating.Height, floating.SurfaceHeight);
        Assert.Equal(attached.ReservedHeight + 14 * scale, floating.ReservedHeight, 8);
        Assert.True(floating.ReservedHeight >= floating.SurfaceHeight + floating.Gap + 8 * scale);
    }

    [Theory]
    [InlineData(.8, 1.25)] [InlineData(.7, 1)] [InlineData(.7, 1.5)] [InlineData(1, 2)]
    public void Reservation_rounds_up_for_monitor_dpi_and_deducts_only_existing_bottom_space(double scale, double dpi)
    {
        var geometry = new DockSizing(scale, 28, PinnedDockPlacement.Floating, 14);
        var pixels = DockSizing.AdditionalReservationPixels(geometry.ReservedHeight, dpi, 10);
        Assert.True(pixels + 10 >= geometry.ReservedHeight * dpi);
        Assert.True(pixels + 10 < geometry.ReservedHeight * dpi + 1);
        // Top reservation is deliberately not an input to bottom reservation.
        Assert.Equal(pixels + 10, DockSizing.AdditionalReservationPixels(geometry.ReservedHeight, dpi, 0));
    }

    [Theory]
    [InlineData(.7)] [InlineData(.8)] [InlineData(1)]
    public void Compact_slots_accommodate_fixed_center_hover_and_geometry_roundtrips(double scale)
    {
        foreach (var size in new[] { 20d, 28, 40 })
        {
            var appearance = new GlassDockSettingsSession(new() { IconSize = size, IconSpacing = 0 }).Appearance.ForDock(scale);
            Assert.True(appearance.ButtonWidth + appearance.IconSpacing >= appearance.IconSize * 1.375 + 2 * scale - 1e-8);
            var height = new DockSizing(scale, size, PinnedDockPlacement.Floating, 14).Height;
            foreach (var p in new[] { 0d, .25, .5, .75, 1 })
            {
                var frame = DockTransitionGeometry.At(p, 500 * scale, height, scale);
                Assert.Equal(p, DockTransitionTiming.Progress(frame.Width, frame.Height, 500 * scale, height, scale), 8);
                Assert.Equal(p * p * (3 - 2 * p), DockIconMotion.FromGeometry(frame.Width, frame.Height, 500 * scale, height, height, scale).Opacity, 8);
            }
        }
    }

    [Fact]
    public void Compact_pointer_targets_keep_fixed_slots_and_return_to_normal_on_exit()
    {
        var appearance = new GlassDockSettingsSession(new()).Appearance.ForDock(.7);
        var pitch = appearance.ButtonWidth + appearance.IconSpacing;
        var row = Enumerable.Range(0, 5).Select(i => new DockHoverMagnification<object>.Item(
            new object(), i * pitch, 0, appearance.ButtonWidth, appearance.ButtonHeight)).ToArray();
        var hover = new DockHoverMagnification<object>();
        hover.SetPointer(2 * pitch + appearance.ButtonWidth / 2, appearance.ButtonHeight / 2);
        var frame = hover.Step(row, 0, false);
        Assert.Equal(new[] { 1d, 1.25, 1.5, 1.25, 1 }, frame.Select(f => f.Scale));
        Assert.All(frame, f => Assert.Equal(0, f.OffsetX));
        hover.Leave();
        Assert.All(hover.Step(row, 1, false), f => { Assert.Equal(1, f.Scale); Assert.Equal(0, f.OffsetX); });
    }

    [Fact]
    public void Old_profiles_keep_original_values_and_new_geometry_defaults_are_bounded()
    {
        var settings = GlassDockSettings.Normalize(new() { IconSize = 32, BottomMargin = 26, FloatingDockGap = double.NaN, DockSize = double.NaN });
        Assert.Equal(.7, settings.DockSize); Assert.Equal(14, settings.FloatingDockGap);
        Assert.Equal(32, settings.IconSize); Assert.Equal(26, settings.BottomMargin);
        Assert.Equal(PinnedDockPlacement.BottomAttached, settings.PinnedPlacement);
    }
}
