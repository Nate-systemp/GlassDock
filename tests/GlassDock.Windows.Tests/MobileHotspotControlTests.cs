using GlassDock.Windows.Desktop;
using Windows.Networking.NetworkOperators;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class MobileHotspotControlTests
{
    [Theory]
    [InlineData(TetheringOperationalState.Off, true)]
    [InlineData(TetheringOperationalState.On, false)]
    public async Task Toggles_current_system_state_once(TetheringOperationalState state, bool expected)
    {
        var writes = new List<bool>();
        var result = await new MobileHotspotControl().ToggleAsync(() => new(() => state, on =>
        { writes.Add(on); return Task.FromResult(new HotspotChangeResult(true, "Completed")); }));
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { expected }, writes);
    }

    [Theory]
    [InlineData(TetheringOperationalState.Unknown)]
    [InlineData(TetheringOperationalState.InTransition)]
    public async Task Unknown_or_pending_state_never_issues_an_operation(TetheringOperationalState state)
    {
        var result = await new MobileHotspotControl().ToggleAsync(() => new(() => state, _ => throw new Xunit.Sdk.XunitException("Must not toggle")));
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Concurrent_click_is_rejected_then_gate_is_released()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<HotspotChangeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new MobileHotspotControl();
        var first = control.ToggleAsync(() => new(() => TetheringOperationalState.Off, _ => { started.SetResult(); return finish.Task; }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await control.ToggleAsync(() => throw new Xunit.Sdk.XunitException("Duplicate resolution"));
        Assert.False(duplicate.Succeeded);
        finish.SetResult(new(true, "Completed"));
        Assert.True((await first).Succeeded);
        Assert.True((await control.ToggleAsync(() => new(() => TetheringOperationalState.On,
            _ => Task.FromResult(new HotspotChangeResult(true, "Completed"))))).Succeeded);
    }

    [Fact]
    public async Task Missing_capability_denial_and_cancellation_are_safe_and_release_gate()
    {
        var control = new MobileHotspotControl();
        Assert.False((await control.ToggleAsync(() => null)).Succeeded);
        Assert.False((await control.ToggleAsync(() => throw new UnauthorizedAccessException())).Succeeded);
        Assert.False((await control.ToggleAsync(() => throw new System.Runtime.InteropServices.COMException())).Succeeded);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.False((await control.ToggleAsync(() => throw new Xunit.Sdk.XunitException("Canceled resolution"), cancellation.Token)).Succeeded);
        Assert.True((await control.ToggleAsync(() => new(() => TetheringOperationalState.Off,
            _ => Task.FromResult(new HotspotChangeResult(true, "Completed"))))).Succeeded);
    }

    [Fact]
    public async Task Failed_native_result_is_not_reported_as_active_or_successful()
    {
        var result = await new MobileHotspotControl().ToggleAsync(() => new(() => TetheringOperationalState.Off,
            _ => Task.FromResult(new HotspotChangeResult(false, "Hardware unavailable"))));
        Assert.False(result.Succeeded);
        Assert.Equal("Hardware unavailable", result.Message);
    }
}
