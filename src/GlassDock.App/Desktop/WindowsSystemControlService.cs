using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace GlassDock.App.Desktop;

internal readonly record struct WindowsSystemSnapshot(
    bool NetworkAvailable,
    string NetworkName,
    int VolumePercent,
    bool Muted,
    bool HasBattery,
    int BatteryPercent,
    bool PluggedIn);

internal sealed class WindowsSystemControlService
{
    public (int Percent, bool Muted)? ReadMasterVolume() => TryGetVolume(out var value, out var muted)
        ? ((int)Math.Round(Math.Clamp(value, 0, 1) * 100), muted) : null;

    public WindowsSystemSnapshot GetSnapshot()
    {
        var networkAvailable = NetworkInterface.GetIsNetworkAvailable();
        var networkName = "Offline";

        if (networkAvailable)
        {
            try
            {
                networkName = NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(adapter =>
                        adapter.OperationalStatus == OperationalStatus.Up &&
                        adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .OrderByDescending(adapter =>
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .Select(adapter => adapter.Name)
                    .FirstOrDefault() ?? "Connected";
            }
            catch (NetworkInformationException)
            {
                networkName = "Connected";
            }
        }

        var volume = TryGetVolume(out var scalar, out var muted)
            ? (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100)
            : 0;

        var hasBattery = false;
        var batteryPercent = 0;
        var pluggedIn = false;

        if (GetSystemPowerStatus(out var power))
        {
            pluggedIn = power.ACLineStatus == 1;
            hasBattery = power.BatteryFlag != 128 &&
                         power.BatteryLifePercent != 255;
            batteryPercent = hasBattery
                ? Math.Clamp(power.BatteryLifePercent, (byte)0, (byte)100)
                : 0;
        }

        return new WindowsSystemSnapshot(
            networkAvailable,
            networkName,
            volume,
            muted,
            hasBattery,
            batteryPercent,
            pluggedIn);
    }

    public bool SetMasterVolume(double value)
    {
        value = Math.Clamp(value, 0, 1);

        try
        {
            using var endpoint = OpenEndpointVolume();
            if (endpoint.Value is null)
                return false;

            var context = Guid.Empty;
            return endpoint.Value.SetMasterVolumeLevelScalar((float)value, ref context) >= 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public bool SetMuted(bool muted)
    {
        try
        {
            using var endpoint = OpenEndpointVolume();
            if (endpoint.Value is null)
                return false;

            var context = Guid.Empty;
            return endpoint.Value.SetMute(muted, ref context) >= 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public void OpenNetworkSettings() => OpenSettings("ms-settings:network-wifi");
    public void OpenBluetoothSettings() => OpenSettings("ms-settings:bluetooth");
    public void OpenFocusSettings() => OpenSettings("ms-settings:quietmomentshome");
    public void OpenAirplaneSettings() => OpenSettings("ms-settings:network-airplanemode");
    public void OpenDisplaySettings() => OpenSettings("ms-settings:display");
    public void OpenNotificationSettings() => OpenSettings("ms-settings:notifications");
    public void OpenTraySettings() => OpenSettings("ms-settings:taskbar");

    private static void OpenSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool TryGetVolume(out float volume, out bool muted)
    {
        volume = 0;
        muted = false;

        try
        {
            using var endpoint = OpenEndpointVolume();
            if (endpoint.Value is null)
                return false;

            return endpoint.Value.GetMasterVolumeLevelScalar(out volume) >= 0 &&
                   endpoint.Value.GetMute(out muted) >= 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static ComLease<IAudioEndpointVolume> OpenEndpointVolume()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)
                Activator.CreateInstance(
                    Type.GetTypeFromCLSID(
                        new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),
                        throwOnError: true)!)!;

            if (enumerator.GetDefaultAudioEndpoint(
                    EDataFlow.Render,
                    ERole.Multimedia,
                    out device) < 0)
            {
                return new ComLease<IAudioEndpointVolume>(null);
            }

            var iid = typeof(IAudioEndpointVolume).GUID;

            if (device.Activate(
                    ref iid,
                    23,
                    nint.Zero,
                    out var endpointObject) < 0)
            {
                return new ComLease<IAudioEndpointVolume>(null);
            }

            endpoint = (IAudioEndpointVolume)endpointObject;
            return new ComLease<IAudioEndpointVolume>(endpoint);
        }
        finally
        {
            if (device is not null && Marshal.IsComObject(device))
                Marshal.ReleaseComObject(device);

            if (enumerator is not null && Marshal.IsComObject(enumerator))
                Marshal.ReleaseComObject(enumerator);
        }
    }

    private sealed class ComLease<T>(T? value) : IDisposable where T : class
    {
        public T? Value { get; } = value;

        public void Dispose()
        {
            if (Value is not null && Marshal.IsComObject(Value))
                Marshal.ReleaseComObject(Value);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out nint devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(nint client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(nint client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid iid,
            uint context,
            nint activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(uint access, out nint properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }
}
