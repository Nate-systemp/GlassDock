using Windows.Devices.Radios;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;
using Windows.UI.Shell;

namespace GlassDock.Windows.Desktop;

public sealed record RadioStatus(string Text, bool IsOn, bool Available);
public sealed record KnownBluetoothDevice(string Name, bool Connected);

/// <summary>Permission-aware radio controls; never infers radio state from Internet availability.</summary>
public sealed class SystemRadioControls
{
    public async Task<RadioStatus> ReadAsync(RadioKind kind)
    {
        try
        {
            var radios = (await Radio.GetRadiosAsync()).Where(r => r.Kind == kind).ToArray();
            if (radios.Length == 0) return new("Unavailable", false, false);
            var on = radios.Count(r => r.State == RadioState.On);
            return new(on == radios.Length ? "On" : on > 0 ? "Mixed" :
                radios.All(r => r.State == RadioState.Off) ? "Off" : "Unavailable", on > 0, true);
        }
        catch (Exception) { return new("Unavailable", false, false); }
    }

    public async Task<string> ToggleAsync(RadioKind kind, CancellationToken cancellation = default)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
                return "Windows denied radio control. Use the secondary Settings action.";
            cancellation.ThrowIfCancellationRequested();
            var radios = (await Radio.GetRadiosAsync()).Where(r => r.Kind == kind).ToArray();
            if (radios.Length == 0) return "No supported radio is available.";
            var desired = radios.Any(r => r.State == RadioState.On) ? RadioState.Off : RadioState.On;
            foreach (var radio in radios)
            {
                cancellation.ThrowIfCancellationRequested();
                if (await radio.SetStateAsync(desired) != RadioAccessStatus.Allowed)
                    return "Windows or the hardware blocked this change.";
            }
            // SetStateAsync queues the request. Never display the requested state as the actual state.
            return "Change requested; current radio state is shown on the tile.";
        }
        catch (Exception) { return "Radio control is unavailable. Use the secondary Settings action."; }
    }

    public async Task<IReadOnlyList<KnownBluetoothDevice>> ReadPairedAsync()
    {
        var properties = new[] { "System.Devices.Aep.IsConnected" };
        var classic = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), properties);
        var le = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), properties);
        return classic.Concat(le).GroupBy(d => d.Id).Select(g => g.First())
            .Select(d => new KnownBluetoothDevice(string.IsNullOrWhiteSpace(d.Name) ? "Bluetooth device" : d.Name,
                d.Properties.TryGetValue(properties[0], out var value) && value is true))
            .OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToArray();
    }

    public static string FocusStatus()
    {
        try
        {
            if (ApiInformation.IsTypePresent("Windows.UI.Shell.FocusSessionManager") && FocusSessionManager.IsSupported)
                return FocusSessionManager.GetDefault().IsFocusActive ? "On" : "Off";
        }
        catch (Exception) { }
        return "Unavailable";
    }
}
