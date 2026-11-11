using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class AirplaneModeUtilityTests
{
    [Fact]
    public async Task Click_waits_for_hover_refresh_instead_of_being_dropped()
    {
        using var releaseRefresh = new ManualResetEventSlim();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toggles = 0;
        using var service = new AirplaneModeUtilityService((toggle, cancel) =>
        {
            if (!toggle) { reading.TrySetResult(); releaseRefresh.Wait(cancel); }
            else Interlocked.Increment(ref toggles);
            return new(toggle);
        });
        var refresh = service.RefreshAsync();
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var click = service.ToggleAsync();
        try { Assert.False(click.IsCompleted); }
        finally { releaseRefresh.Set(); }
        await Task.WhenAll(refresh, click).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, toggles);
        Assert.True(service.State.On);
        Assert.False(service.State.Busy);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void Global_radio_enabled_is_the_inverse_of_airplane_mode(bool airplaneOn, int radioEnabled)
    {
        Assert.Equal(radioEnabled, AirplaneModeNative.RadioEnabledForAirplane(airplaneOn));
        Assert.Equal(airplaneOn, AirplaneModeNative.AirplaneFromRadioEnabled(radioEnabled));
        Assert.Throws<NotSupportedException>(() => AirplaneModeNative.AirplaneFromRadioEnabled(2));
    }

    [Fact]
    public async Task Repeated_clicks_do_not_queue_during_native_work_or_presentation()
    {
        using var releaseNative = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var service = new AirplaneModeUtilityService((toggle, cancel) =>
        {
            Interlocked.Increment(ref calls); entered.TrySetResult();
            releaseNative.Wait(cancel);
            return new(true);
        });
        var first = service.ToggleAsync(async () => { settling.TrySetResult(); await finish.Task; });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await service.ToggleAsync(); await service.RefreshAsync();
            Assert.Equal(1, calls);
            releaseNative.Set();
            await settling.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(service.State.Busy);
            Assert.True(service.State.On);
            await service.ToggleAsync();
            Assert.Equal(1, calls);
        }
        finally { releaseNative.Set(); finish.TrySetResult(); await first; }
        Assert.False(service.State.Busy);
    }

    [Fact]
    public async Task Failure_preserves_recovered_state_and_refresh_observes_external_changes()
    {
        using var service = new AirplaneModeUtilityService((toggle, _) => toggle
            ? new(true, new UnauthorizedAccessException()) : new(false));
        await service.ToggleAsync();
        Assert.True(service.State.On);
        Assert.False(service.State.Available);
        Assert.Contains("denied", service.State.Message);
        Assert.False(service.State.Busy);
        await service.RefreshAsync();
        Assert.False(service.State.On);
        Assert.True(service.State.Available);
    }

    [Fact]
    public async Task Timeout_quarantines_private_api_and_never_claims_an_off_state()
    {
        var calls = 0;
        using var service = new AirplaneModeUtilityService((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return new(null, new TimeoutException());
        });
        await service.ToggleAsync();
        Assert.Null(service.State.On);
        Assert.False(service.State.Available);
        Assert.False(service.State.Busy);
        await service.ToggleAsync(); await service.RefreshAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Disposed_service_never_calls_native_code()
    {
        var calls = 0;
        var service = new AirplaneModeUtilityService((_, _) => { calls++; return new(false); });
        service.Dispose(); service.Dispose();
        await service.RefreshAsync(); await service.ToggleAsync();
        Assert.Equal(0, calls);
    }
}
