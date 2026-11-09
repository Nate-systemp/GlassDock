using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class EnergySaverUtilityTests
{
    [Theory]
    [InlineData(true, 0u, null, false, 1u)]
    [InlineData(true, 2u, null, false, 1u)]
    [InlineData(false, 1u, 0u, false, 0u)]
    [InlineData(false, 1u, 2u, false, 2u)]
    [InlineData(false, 1u, null, false, 2u)]
    [InlineData(false, 1u, 2u, true, 0u)]
    [InlineData(false, 2u, null, true, 0u)]
    public void Off_restores_automatic_only_when_it_will_not_reengage_saver(
        bool on, uint current, uint? previous, bool automatic, uint expected)
    {
        Assert.Equal(expected, EnergySaverNative.SelectOverride(on, current, previous, automatic));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    public void Independent_notification_accepts_all_known_effective_states(uint status)
    {
        Assert.Equal(status, EnergySaverStatusSubscription.ParseStatus(EnergySaverStatusSubscription.StatusGuid, 4, status));
    }

    [Fact]
    public void Unknown_notification_formats_and_override_values_are_rejected()
    {
        Assert.Throws<NotSupportedException>(() => EnergySaverStatusSubscription.ParseStatus(Guid.Empty, 4, 1));
        Assert.Throws<NotSupportedException>(() => EnergySaverStatusSubscription.ParseStatus(EnergySaverStatusSubscription.StatusGuid, 1, 1));
        Assert.Throws<NotSupportedException>(() => EnergySaverStatusSubscription.ParseStatus(EnergySaverStatusSubscription.StatusGuid, 4, 3));
        Assert.Throws<NotSupportedException>(() => EnergySaverNative.SelectOverride(true, 3, null, false));
        Assert.Throws<NotSupportedException>(() => EnergySaverNative.SelectOverride(false, 1, 3, false));
    }
}
