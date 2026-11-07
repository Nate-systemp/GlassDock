using GlassDock.Core.Desktop;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class BluetoothUtilityTests
{
    private static BluetoothDeviceInfo Device(string id, bool connected = false, int? battery = null) =>
        new(id, id, BluetoothDeviceKind.Generic, connected, battery, true, false);

    [Fact]
    public void Only_paired_devices_survive_updates()
    {
        var store = new BluetoothDeviceStore();
        store.Update(Device("unpaired"), false);
        store.Update(Device("paired"), true);
        Assert.Equal("paired", Assert.Single(store.Snapshot).Id);
        store.Update(Device("paired"), false);
        Assert.Empty(store.Snapshot);
    }

    [Fact]
    public void Connection_and_battery_updates_preserve_order_and_previous_snapshot()
    {
        var store = new BluetoothDeviceStore();
        store.Update(Device("first"), true);
        store.Update(Device("second"), true);
        var before = store.Snapshot;
        store.Update(Device("second", true, 42), true);
        Assert.Equal(new[] { "first", "second" }, store.Snapshot.Select(d => d.Id));
        Assert.False(before[1].IsConnected);
        Assert.Null(before[1].BatteryPercent);
        Assert.True(store.Snapshot[1].IsConnected);
    }

    [Fact]
    public void Connect_request_does_not_claim_connection_and_duplicate_is_ignored()
    {
        var store = new BluetoothDeviceStore();
        store.Update(Device("headset"), true);
        var token = store.Begin("headset", "Connecting...")!.Value;
        Assert.Null(store.Begin("headset", "Connecting..."));
        Assert.True(store.Find("headset")!.Busy);
        Assert.False(store.Find("headset")!.IsConnected);
        Assert.True(store.Complete("headset", token, "Connection failed"));
        Assert.False(store.Find("headset")!.IsConnected);
        Assert.Equal("Connection failed", store.Find("headset")!.Status);
        Assert.False(store.Find("headset")!.Busy);
        Assert.NotNull(store.Begin("headset", "Connecting..."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Radio_off_or_removal_invalidates_old_completions_after_same_id_returns(bool remove)
    {
        var store = new BluetoothDeviceStore();
        store.Update(Device("mouse"), true);
        var stale = store.Begin("mouse", "Connecting...")!.Value;
        if (remove) store.Remove("mouse"); else store.Clear();
        store.Update(Device("mouse"), true);
        var current = store.Begin("mouse", "Connecting...")!.Value;
        Assert.False(store.Complete("mouse", stale, "Connection failed"));
        Assert.True(store.Find("mouse")!.Busy);
        store.Update(Device("mouse", true), true);
        Assert.True(store.Complete("mouse", current, null));
        Assert.True(store.Find("mouse")!.IsConnected);
        Assert.False(store.Find("mouse")!.Busy);
    }

    [Fact]
    public void Battery_update_preserves_failure_until_actual_connection_changes()
    {
        var store = new BluetoothDeviceStore();
        store.Update(Device("speaker"), true);
        var token = store.Begin("speaker", "Connecting...")!.Value;
        store.Update(Device("speaker", battery: 50), true);
        Assert.Equal("Connecting...", store.Find("speaker")!.Status);
        store.Complete("speaker", token, "Connection failed");
        store.Update(Device("speaker", battery: 51), true);
        Assert.Equal("Connection failed", store.Find("speaker")!.Status);
        store.Update(Device("speaker", true, 51), true);
        Assert.Null(store.Find("speaker")!.Status);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, null)]
    [InlineData(255, null)]
    public void Battery_accepts_only_real_bounded_numeric_properties(int value, int? expected)
    {
        Assert.Equal(expected, BluetoothUtilityService.ReadBattery(value));
        Assert.Null(BluetoothUtilityService.ReadBattery(value.ToString()));
        var store = new BluetoothDeviceStore();
        store.Update(Device("device", battery: value), true);
        Assert.Equal(expected, Assert.Single(store.Snapshot).BatteryPercent);
    }

    [Fact]
    public void Battery_supports_windows_byte_property_and_rejects_unknown_sentinel()
    {
        Assert.Equal(72, BluetoothUtilityService.ReadBattery((byte)72));
        Assert.Null(BluetoothUtilityService.ReadBattery((byte)101));
        Assert.Null(BluetoothUtilityService.ReadBattery(null));
    }

    [Theory]
    [InlineData(4, 6, BluetoothDeviceKind.Headphones)]
    [InlineData(4, 1, BluetoothDeviceKind.Headset)]
    [InlineData(4, 5, BluetoothDeviceKind.Speaker)]
    [InlineData(5, 32, BluetoothDeviceKind.Mouse)]
    [InlineData(5, 16, BluetoothDeviceKind.Keyboard)]
    [InlineData(5, 2, BluetoothDeviceKind.Controller)]
    [InlineData(1, 1, BluetoothDeviceKind.Generic)]
    public void Classic_kind_uses_reported_class_instead_of_device_name(int major, int minor, BluetoothDeviceKind expected)
    {
        Assert.Equal(expected, BluetoothUtilityService.DeviceKind(new Dictionary<string, object>
        {
            ["System.Devices.Aep.Bluetooth.Cod.Major"] = major,
            ["System.Devices.Aep.Bluetooth.Cod.Minor"] = minor
        }));
    }

    [Fact]
    public async Task Missing_container_never_targets_an_unrelated_audio_device()
    {
        Assert.Null(await SystemControlAudio.SetBluetoothConnectionAsync(Guid.Empty, true, CancellationToken.None));
        Assert.Null(await SystemControlAudio.SetBluetoothConnectionAsync(Guid.Empty, false, CancellationToken.None));
    }

    [Fact]
    public async Task Constructor_and_disposed_operations_are_lazy_and_do_not_touch_hardware()
    {
        var service = new BluetoothUtilityService();
        Assert.False(service.State.Available);
        Assert.Empty(service.Devices);
        service.Dispose(); service.Dispose();
        await service.RefreshAsync(); await service.ToggleAsync();
        await service.ConnectAsync("missing"); await service.DisconnectAsync("missing"); await service.RemoveAsync("missing");
        Assert.Empty(service.Devices);
    }
}
