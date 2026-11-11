using GlassDock.Core.Settings;
using GlassDock.Core.Materials;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockAppRowSizingTests
{
    private static DockAppearanceSettings Appearance => new(GlassMaterialMode.Frosted, 28, 1.24, 6, 20, .8, 0, 0);

    [Theory]
    [InlineData(1, 800, .7)]
    [InlineData(30, 800, .7)]
    [InlineData(60, 1600, 1)]
    [InlineData(200, 1200, .8)]
    [InlineData(1000, 600, 1)]
    public void Entire_app_row_fits_inside_the_surface(int count, double width, double scale)
    {
        var original = Appearance.ForDock(scale);
        var padding = 36 * scale;
        var fit = original.FitAppRow(count, width, padding);
        var row = count * fit.ButtonWidth + (count - 1) * fit.IconSpacing;
        Assert.True(row + padding <= width + .000001);
        Assert.InRange(fit.IconSize, double.Epsilon, original.IconSize);
        Assert.Equal(fit.IconSize / original.IconSize, fit.LayoutScale / original.LayoutScale, 8);
    }

    [Fact]
    public void Existing_icon_size_is_preserved_until_the_monitor_is_full()
    {
        var appearance = Appearance.ForDock(.7);
        Assert.Equal(appearance, appearance.FitAppRow(10, 1200, 36 * .7));
        Assert.Equal(appearance, appearance.FitAppRow(0, 1200, 36 * .7));
    }
}
