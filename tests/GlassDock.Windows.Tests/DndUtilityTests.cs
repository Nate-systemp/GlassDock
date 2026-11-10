using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DndUtilityTests
{
    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, true)]
    [InlineData(2u, true)]
    public void Actual_mode_recognizes_both_restricted_modes(uint mode, bool expected)
        => Assert.Equal(expected, DndNative.IsOn(mode));

    [Theory]
    [InlineData(0u, "Microsoft.QuietHoursProfile.PriorityOnly")]
    [InlineData(1u, "Microsoft.QuietHoursProfile.Unrestricted")]
    [InlineData(2u, "Microsoft.QuietHoursProfile.Unrestricted")]
    public void Toggle_target_comes_from_actual_mode_even_for_an_alarms_rule(uint mode, string expected)
        => Assert.Equal(expected, DndNative.ProfileFor(!DndNative.IsOn(mode)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Microsoft.QuietHoursProfile.Future")]
    public void Unknown_profiles_are_rejected_before_writing(string? profile)
        => Assert.Throws<NotSupportedException>(() => DndNative.ValidateProfile(profile));

    [Fact]
    public void Unknown_effective_mode_is_not_interpreted_as_on_or_off()
    {
        Assert.Throws<NotSupportedException>(() => DndNative.IsOn(3));
        DndNative.ValidateProfile(DndNative.OffProfile);
        DndNative.ValidateProfile(DndNative.OnProfile);
        DndNative.ValidateProfile(DndNative.AlarmsProfile);
    }
}
