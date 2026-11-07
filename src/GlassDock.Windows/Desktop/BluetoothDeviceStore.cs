using GlassDock.Core.Desktop;

namespace GlassDock.Windows.Desktop;

// The service lock owns this state, including operation generations and insertion order.
internal sealed class BluetoothDeviceStore
{
    private readonly Dictionary<string, BluetoothDeviceInfo> devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> pending = new(StringComparer.Ordinal);
    private long generation;

    public IReadOnlyList<BluetoothDeviceInfo> Snapshot => Array.AsReadOnly(devices.Values.ToArray());
    public BluetoothDeviceInfo? Find(string id) => devices.GetValueOrDefault(id);

    public void Update(BluetoothDeviceInfo device, bool paired)
    {
        if (!paired) { Remove(device.Id); return; }
        device = device with { BatteryPercent = ValidBattery(device.BatteryPercent) };
        if (devices.TryGetValue(device.Id, out var old))
        {
            if (pending.ContainsKey(device.Id)) device = device with { Busy = true, Status = old.Status };
            else if (old.IsConnected == device.IsConnected)
                device = device with { Status = old.Status };
        }
        devices[device.Id] = device;
    }

    public long? Begin(string id, string message)
    {
        if (!devices.TryGetValue(id, out var device) || pending.ContainsKey(id)) return null;
        var token = ++generation;
        pending[id] = token;
        devices[id] = device with { Busy = true, Status = message };
        return token;
    }

    public bool Complete(string id, long token, string? message)
    {
        if (!pending.TryGetValue(id, out var current) || current != token) return false;
        pending.Remove(id);
        devices[id] = devices[id] with { Busy = false, Status = message };
        return true;
    }

    public void Remove(string id) { devices.Remove(id); pending.Remove(id); }
    public void Clear() { devices.Clear(); pending.Clear(); }
    public static int? ValidBattery(int? value) => value is >= 0 and <= 100 ? value : null;
}
