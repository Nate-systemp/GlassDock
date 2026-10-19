using GlassDock.Windows.Desktop;
using System.Runtime.InteropServices;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class SystemControlAutomationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Uses_actual_state_and_verifies_one_command(bool initial)
    {
        bool? actual = initial;
        var writes = new List<bool>();
        var disposed = 0;
        var result = SystemControlAutomation.Change(() => new(() => actual, value => { writes.Add(value); actual = value; }, () => disposed++),
            () => throw new Xunit.Sdk.XunitException("Existing control must be reused"), default, 2, 0);
        Assert.True(result.Succeeded);
        Assert.Equal(!initial, result.State);
        Assert.Equal(new[] { !initial }, writes);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void Unknown_state_is_not_off_and_does_not_toggle()
    {
        var disposed = false;
        var result = SystemControlAutomation.Change(() => new(() => null, _ => throw new Xunit.Sdk.XunitException("Unknown must not toggle"), () => disposed = true),
            () => throw new Xunit.Sdk.XunitException("No navigation expected"), default, 1, 0);
        Assert.False(result.Succeeded);
        Assert.Null(result.State);
        Assert.True(disposed);
    }

    [Fact]
    public void Opens_native_page_once_and_waits_for_unique_ready_control()
    {
        var resolutions = 0; var opened = 0; var writes = 0; bool? state = false;
        var result = SystemControlAutomation.Change(() => ++resolutions < 3 ? null : new(() => state, on => { writes++; state = on; }, () => { }),
            () => opened++, default, 4, 0);
        Assert.True(result.Succeeded);
        Assert.Equal(1, opened);
        Assert.Equal(1, writes);
    }

    [Fact]
    public void Unavailable_page_fails_without_a_command()
    {
        var opened = 0;
        var result = SystemControlAutomation.Change(() => null, () => opened++, default, 2, 0);
        Assert.False(result.Succeeded);
        Assert.Null(result.State);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void Unconfirmed_write_is_never_retried_or_shown_as_on()
    {
        var writes = 0;
        var result = SystemControlAutomation.Change(() => new(() => false, _ => writes++, () => { }), () => { }, default, 3, 0);
        Assert.False(result.Succeeded);
        Assert.Null(result.State);
        Assert.Equal(1, writes);
    }

    [Fact]
    public void Delayed_windows_confirmation_does_not_repeat_toggle()
    {
        var reads = 0; var writes = 0;
        var result = SystemControlAutomation.Change(() => new(() => ++reads >= 4, _ => writes++, () => { }), () => { }, default, 4, 0);
        Assert.True(result.Succeeded);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Provider_failure_after_invoke_disposes_endpoint_and_is_not_retried()
    {
        var writes = 0; var disposed = 0;
        var result = await new SystemControlAutomation().ExecuteAsync(() => SystemControlAutomation.Change(
            () => new(() => writes == 0 ? false : throw new COMException(), _ => writes++, () => disposed++), () => { }, default, 2, 0));
        Assert.False(result.Succeeded);
        Assert.Null(result.State);
        Assert.Equal(1, writes);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task Duplicate_click_rejected_and_gate_recovers_after_failure()
    {
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var service = new SystemControlAutomation();
        var first = service.ExecuteAsync(() => { started.Set(); Assert.True(finish.Wait(TimeSpan.FromSeconds(5))); throw new COMException(); });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var duplicate = await service.ExecuteAsync(() => throw new Xunit.Sdk.XunitException("Duplicate command"));
            Assert.False(duplicate.Succeeded);
        }
        finally { finish.Set(); }
        Assert.False((await first).Succeeded);
        Assert.True((await service.ExecuteAsync(() => new(true, true, "Verified"))).Succeeded);
    }

    [Fact]
    public async Task Cancellation_before_command_never_navigates_and_after_command_never_reverses()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new SystemControlAutomation();
        Assert.False((await service.ExecuteAsync(() => throw new Xunit.Sdk.XunitException("Canceled operation"), cancellation.Token)).Succeeded);
        using var afterWrite = new CancellationTokenSource();
        var writes = 0;
        var result = SystemControlAutomation.Change(() => new(() => false, _ => { writes++; afterWrite.Cancel(); }, () => { }),
            () => { }, afterWrite.Token, 2, 0);
        Assert.False(result.Succeeded);
        Assert.Null(result.State);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Popup_shutdown_disposing_cancellation_source_during_command_is_safe()
    {
        var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        var result = await new SystemControlAutomation().ExecuteAsync(() => SystemControlAutomation.Change(
            () => new(() => false, _ => { lifetime.Cancel(); lifetime.Dispose(); }, () => { }), () => { }, token, 2, 0), token);
        Assert.False(result.Succeeded);
        Assert.Null(result.State);
    }

    [Fact]
    public void Ambiguous_disabled_and_offscreen_controls_cannot_be_invoked()
    {
        var target = new WindowsControlObservation("Airplane mode", SystemControlAutomation.AirplaneId, 50000, true, false, false);
        Assert.Equal(0, SystemControlAutomation.SelectTarget(WindowsQuickControl.Airplane, [target]));
        Assert.Equal(-1, SystemControlAutomation.SelectTarget(WindowsQuickControl.Airplane, [target, target]));
        Assert.Equal(-1, SystemControlAutomation.SelectTarget(WindowsQuickControl.Airplane, [target with { Enabled = false }]));
        Assert.Equal(-1, SystemControlAutomation.SelectTarget(WindowsQuickControl.Airplane, [target with { Offscreen = true }]));
        Assert.Equal(0, SystemControlAutomation.SelectTarget(WindowsQuickControl.Airplane, [target with { Offscreen = true }], requireVisible: false));
        Assert.Equal(-1, SystemControlAutomation.SelectTarget(WindowsQuickControl.Airplane, [target with { Enabled = false }], requireVisible: false));
        Assert.Equal(-1, SystemControlAutomation.SelectTarget(WindowsQuickControl.Airplane, [target, target], requireVisible: false));
        Assert.Equal(-1, SystemControlAutomation.SelectTarget(WindowsQuickControl.NightLight, [target]));
    }

    [Fact]
    public void Selectors_do_not_substitute_dnd_radio_or_scheduling_controls()
    {
        WindowsControlObservation Item(string id, int type = 50000) => new("Any localized text", id, type, true, false, false);
        Assert.True(SystemControlAutomation.Matches(WindowsQuickControl.Focus, Item(SystemControlAutomation.FocusStartId)));
        Assert.True(SystemControlAutomation.Matches(WindowsQuickControl.Focus, Item(SystemControlAutomation.FocusStopId)));
        Assert.False(SystemControlAutomation.Matches(WindowsQuickControl.Focus, Item("SystemSettings_FocusAssist_FocusTheme_MuteNotifications_Enabled_CheckBox", 50002)));
        Assert.True(SystemControlAutomation.Matches(WindowsQuickControl.NightLight, Item(SystemControlAutomation.NightLightId)));
        Assert.False(SystemControlAutomation.Matches(WindowsQuickControl.NightLight, Item("SystemSettings_Display_BlueLight_Schedule_ToggleSwitch")));
        Assert.True(SystemControlAutomation.Matches(WindowsQuickControl.Airplane, Item(SystemControlAutomation.AirplaneId)));
        Assert.False(SystemControlAutomation.Matches(WindowsQuickControl.Airplane, Item("WiFiRadioToggle")));
        Assert.False(SystemControlAutomation.Matches(WindowsQuickControl.Airplane, Item(SystemControlAutomation.AirplaneId, 50020)));
        Assert.Equal("ms-settings:quiethours", SystemControlAutomation.Page(WindowsQuickControl.Focus));
    }
}
