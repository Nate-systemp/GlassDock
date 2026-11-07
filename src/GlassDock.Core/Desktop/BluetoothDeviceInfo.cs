namespace GlassDock.Core.Desktop;

public enum BluetoothDeviceKind { Generic, Headphones, Headset, Mouse, Keyboard, Controller, Speaker }

public sealed record BluetoothDeviceInfo(
    string Id, string Name, BluetoothDeviceKind Kind, bool IsConnected, int? BatteryPercent,
    bool CanConnect, bool CanDisconnect, string? Status = null, bool Busy = false);
