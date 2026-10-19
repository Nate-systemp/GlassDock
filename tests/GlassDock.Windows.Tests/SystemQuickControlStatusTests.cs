using GlassDock.Windows.Desktop;
using System.Runtime.InteropServices;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class SystemQuickControlStatusTests
{
    [Theory]
    [InlineData(global::Windows.System.Power.EnergySaverStatus.On, true)]
    [InlineData(global::Windows.System.Power.EnergySaverStatus.Off, false)]
    [InlineData(global::Windows.System.Power.EnergySaverStatus.Disabled, false)]
    public void Saver_disabled_while_plugged_in_is_known_inactive(global::Windows.System.Power.EnergySaverStatus value, bool active)
        => Assert.Equal(active, SystemQuickControlStatus.EnergySaverEnabled(value));

    [Fact]
    public void Active_hotspot_on_any_adapter_wins_over_unknown_or_off_adapters()
    {
        Assert.True(SystemQuickControlStatus.Combine([null, false, true]));
        Assert.True(SystemQuickControlStatus.Combine([true, null]));
    }

    [Fact]
    public void Off_requires_all_profile_states_to_be_known()
    {
        Assert.False(SystemQuickControlStatus.Combine([false, false]));
        Assert.Null(SystemQuickControlStatus.Combine([false, null]));
        Assert.Null(SystemQuickControlStatus.Combine([]));
    }

    [Fact]
    public void Permission_and_transient_failures_are_unknown_not_off_or_active()
    {
        Assert.Null(SystemQuickControlStatus.ReadSafely(() => throw new UnauthorizedAccessException()));
        Assert.Null(SystemQuickControlStatus.ReadSafely(() => throw new COMException("Profile removed")));
        Assert.Null(SystemQuickControlStatus.ReadSafely(() => throw new InvalidOperationException()));
        Assert.Null(SystemQuickControlStatus.ReadSafely(() => throw new NotSupportedException()));
    }

    [Fact]
    public void Every_refresh_reads_current_state_including_failure_and_recovery()
    {
        bool? current = false;
        bool? Read() => current;
        Assert.False(SystemQuickControlStatus.ReadSafely(Read));
        current = true;
        Assert.True(SystemQuickControlStatus.ReadSafely(Read));
        current = null;
        Assert.Null(SystemQuickControlStatus.ReadSafely(Read));
        current = false;
        Assert.False(SystemQuickControlStatus.ReadSafely(Read));
    }
}
