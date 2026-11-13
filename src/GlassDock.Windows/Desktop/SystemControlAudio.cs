using System.Runtime.InteropServices;
namespace GlassDock.Windows.Desktop;

internal sealed partial class SystemControlAudio
{
    private static readonly Type? MmDeviceEnumeratorType =
        Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: false);
    public (int Percent, bool Muted)? Read() => TryGetVolume(out var value, out var muted)
        ? ((int)Math.Round(Math.Clamp(value, 0, 1) * 100), muted) : null;
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
        if (MmDeviceEnumeratorType is null)
            return new ComLease<IAudioEndpointVolume>(null);

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(MmDeviceEnumeratorType)!;

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
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

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
        int OpenPropertyStore(uint access, out Applications.ApplicationNative.IPropertyStore properties);

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
