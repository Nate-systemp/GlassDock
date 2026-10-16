using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilitiesPressScaleTests
{
    [Fact]
    public void Press_release_and_long_hold_have_exact_endpoints()
    {
        var scale = new UtilitiesPressScale();
        scale.Set(true, 1000, true);
        Assert.Equal(1, scale.At(1000));
        Assert.Equal(1.2, scale.At(1180), 8);
        Assert.Equal(1.2, scale.At(1600), 8);
        scale.Set(false, 1600, true);
        Assert.Equal(240, scale.Duration);
        Assert.Equal(1, scale.At(1840), 8);
    }
    [Fact]
    public void Early_release_cancel_and_repeated_press_are_continuous()
    {
        var scale = new UtilitiesPressScale();
        scale.Set(true, 100, true);
        var halfway = scale.At(190);
        Assert.False(scale.Set(true, 190, true));
        scale.Set(false, 190, true);
        Assert.Equal(halfway, scale.At(190));
        var returning = scale.At(240);
        scale.Set(true, 240, true);
        Assert.Equal(returning, scale.At(240));
        for (var t = 240; t <= 420; t++) Assert.InRange(scale.At(t), 1, 1.2);
    }
    [Theory]
    [InlineData(102)]
    [InlineData(204)]
    [InlineData(300)]
    public void Fixed_padding_fits_centered_growth(double width)
    {
        Assert.True(UtilitiesPressScale.Padding(width) * 2 + width >= width * 1.2 + 8 - .00001);
    }
    [Fact]
    public void Reduced_motion_is_immediate()
    {
        var scale = new UtilitiesPressScale();
        scale.Set(true, 10, false);
        Assert.Equal(1.2, scale.At(10));
        scale.Set(false, 20, false);
        Assert.Equal(1, scale.At(20));
    }
}
