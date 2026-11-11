using GlassDock.Windows.Desktop;
using Xunit;
using Xunit.Abstractions;

namespace GlassDock.Windows.Tests;

public sealed class BrightnessUtilityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, 50, 100, 50)]
    [InlineData(20, 120, 220, 50)]
    [InlineData(20, 20, 220, 0)]
    [InlineData(20, 220, 220, 100)]
    public void Hardware_ranges_map_to_real_percent_without_assuming_zero_minimum(uint min, uint value, uint max, int percent)
    {
        Assert.Equal(percent, BrightnessRange.Percent(min, value, max));
        Assert.Equal(value, BrightnessRange.NativeValue(percent, min, max));
    }

    [Fact]
    public void Invalid_hardware_ranges_and_values_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => BrightnessRange.Percent(10, 10, 10));
        Assert.Throws<InvalidOperationException>(() => BrightnessRange.Percent(20, 19, 100));
        Assert.Throws<InvalidOperationException>(() => BrightnessRange.Percent(20, 101, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => BrightnessRange.NativeValue(101, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => BrightnessRange.NearestLevel(-1, [0, 100]));
    }

    [Fact]
    public void Wmi_result_supports_boolean_providers_and_status_code_providers()
    {
        Assert.True(BrightnessHardware.WmiWriteAccepted(true));
        Assert.False(BrightnessHardware.WmiWriteAccepted(false));
        Assert.True(BrightnessHardware.WmiWriteAccepted((uint)0));
        Assert.False(BrightnessHardware.WmiWriteAccepted((uint)5));
        Assert.True(BrightnessHardware.WmiWriteAccepted(null));
        Assert.False(BrightnessHardware.WmiWriteAccepted("true"));
    }

    [Fact]
    public void Wmi_levels_are_quantized_to_the_reported_hardware_steps()
    {
        Assert.Equal((byte)60, BrightnessRange.NearestLevel(63, [0, 20, 40, 60, 80, 100]));
        Assert.Equal((byte)63, BrightnessRange.NearestLevel(63, []));
        Assert.Equal((byte)100, BrightnessRange.NearestLevel(100, [0, 100, 255]));
    }

    [Fact]
    public void Monitor_interface_and_WMI_instance_names_match_without_losing_identity()
    {
        const string interfacePath = @"\\?\DISPLAY#NCP004D#5&1b5e6772&0&UID256#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        Assert.Equal(BrightnessHardware.InstanceKey(interfacePath), BrightnessHardware.InstanceKey(@"DISPLAY\NCP004D\5&1b5e6772&0&UID256_0"));
        Assert.NotEqual(BrightnessHardware.InstanceKey(interfacePath), BrightnessHardware.InstanceKey(@"DISPLAY\NCP004D\5&1b5e6772&0&UID257_0"));
    }

    [Fact]
    public void Drag_writes_keep_only_latest_value_per_monitor_and_drop_removed_endpoints()
    {
        var pending = new BrightnessWriteQueue();
        for (var i = 0; i <= 100; i++) pending.Request("panel", i);
        pending.Request("external", 35);
        Assert.Equal(new BrightnessWriteQueue.Requested("panel", 100), pending.Take());
        pending.Request("panel", 20);
        pending.Keep(new HashSet<string> { "panel" });
        Assert.Equal(new BrightnessWriteQueue.Requested("panel", 20), pending.Take());
        Assert.False(pending.HasPending);
        Assert.Null(pending.Take());
    }

    [Fact]
    public void Coalescing_keeps_the_final_request_revision_for_readback_completion()
    {
        var pending = new BrightnessWriteQueue();
        pending.Request("panel", 30, 1);
        pending.Request("panel", 70, 2);
        Assert.Equal(new BrightnessWriteQueue.Requested("panel", 70, 2), pending.Take());
    }

    [Fact]
    public async Task Passive_discovery_reports_hardware_state_or_explicit_unavailability_without_writes()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new BrightnessService();
        service.Changed += (_, e) => { if (e.Display is null) ready.TrySetResult(); };
        service.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
        output.WriteLine(service.Status);
        foreach (var display in service.Displays)
        {
            output.WriteLine($"{display.Name}: {display.Backend}, available={display.Available}, percent={display.Percent}");
            if (display.Available) Assert.InRange(display.Percent!.Value, 0, 100);
            else Assert.False(string.IsNullOrWhiteSpace(display.Message));
        }
    }
}
