using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

internal sealed partial class SystemControlAudio
{
    /// <summary>All endpoint/RCW work belongs to the service's MTA worker, never a native callback.</summary>
    internal sealed class VolumeMonitor : IDisposable
    {
        private readonly Action<Action> post;
        private readonly Action<MasterVolumeState, bool> publish;
        private readonly Guid context = Guid.NewGuid();
        private readonly DeviceCallback devices;
        private VolumeCallback? callback;
        private IMMDeviceEnumerator? enumerator;
        private IAudioEndpointVolume? endpoint;
        private string? endpointId;
        private nint devicePointer, volumePointer;
        private bool disposed;

        internal VolumeMonitor(Action<Action> post, Action<MasterVolumeState, bool> publish)
        {
            this.post = post; this.publish = publish;
            devices = new(() => post(() => Bind()));
            try
            {
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(MmDeviceEnumeratorType!)!;
                devicePointer = Marshal.GetComInterfaceForObject(devices, typeof(IDefaultAudioDeviceCallback));
                Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(devicePointer));
                Bind();
            }
            catch { Dispose(); throw; }
        }

        private void Bind()
        {
            if (disposed) return;
            IMMDevice? device = null;
            try
            {
                Marshal.ThrowExceptionForHR(enumerator!.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
                Marshal.ThrowExceptionForHR(device.GetId(out var id));
                Unbind();
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var value));
                endpoint = (IAudioEndpointVolume)value; endpointId = id;
                callback = new(data =>
                {
                    if (data == 0) return;
                    var eventContext = Marshal.PtrToStructure<Guid>(data);
                    // Capture this endpoint's identity; late callbacks cannot update its replacement.
                    post(() => { if (id == endpointId) Read(eventContext != context); });
                });
                volumePointer = Marshal.GetComInterfaceForObject(callback, typeof(IVolumeNotificationCallback));
                Marshal.ThrowExceptionForHR(endpoint.RegisterControlChangeNotify(volumePointer));
                Read(false);
            }
            catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException)
            {
                Unbind(); publish(new(0, false, false, null, $"Audio output unavailable: {error.Message}"), false);
            }
            finally { if (device is not null) Marshal.ReleaseComObject(device); }
        }

        private void Read(bool external)
        {
            if (disposed || endpoint is null) return;
            try
            {
                Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var volume));
                Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
                if (!float.IsFinite(volume) || volume is < 0 or > 1) throw new InvalidOperationException("Invalid master volume.");
                publish(new(volume, muted, true, endpointId, "Master output volume"), external);
            }
            catch (Exception error) when (error is COMException or InvalidOperationException)
            { publish(new(0, false, false, endpointId, $"Audio output unavailable: {error.Message}"), false); }
        }

        internal void SetVolume(float value, string? expectedEndpoint)
        {
            if (disposed || endpoint is null || endpointId != expectedEndpoint) return;
            try
            {
                var eventContext = context;
                Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(Math.Clamp(value, 0, 1), ref eventContext));
                if (value > 0) Marshal.ThrowExceptionForHR(endpoint.SetMute(false, ref eventContext));
                Read(false);
            }
            catch (COMException error)
            {
                Read(false);
                System.Diagnostics.Debug.WriteLine($"Master volume update failed: {error.Message}");
            }
        }

        internal bool HandleVolumeKey(uint key, string? expectedEndpoint)
        {
            if (disposed || endpoint is null || endpointId != expectedEndpoint) return false;
            try
            {
                var eventContext = context;
                if (key == 0xAD)
                {
                    Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
                    Marshal.ThrowExceptionForHR(endpoint.SetMute(!muted, ref eventContext));
                }
                else
                {
                    Marshal.ThrowExceptionForHR(key == 0xAF
                        ? endpoint.VolumeStepUp(ref eventContext)
                        : endpoint.VolumeStepDown(ref eventContext));
                    Marshal.ThrowExceptionForHR(endpoint.SetMute(false, ref eventContext));
                }
                Read(false);
                return true;
            }
            catch (COMException error)
            {
                publish(new(0, false, false, endpointId, $"Volume key failed: {error.Message}"), false);
                return false;
            }
        }

        private void Unbind()
        {
            if (endpoint is not null)
            {
                try { if (volumePointer != 0) endpoint.UnregisterControlChangeNotify(volumePointer); }
                catch (COMException error) { System.Diagnostics.Debug.WriteLine(error); }
                finally { Marshal.ReleaseComObject(endpoint); endpoint = null; }
            }
            if (volumePointer != 0) { Marshal.Release(volumePointer); volumePointer = 0; }
            callback = null; endpointId = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unbind();
            if (enumerator is not null)
            {
                try { if (devicePointer != 0) enumerator.UnregisterEndpointNotificationCallback(devicePointer); }
                catch (COMException error) { System.Diagnostics.Debug.WriteLine(error); }
                finally { Marshal.ReleaseComObject(enumerator); enumerator = null; }
            }
            if (devicePointer != 0) { Marshal.Release(devicePointer); devicePointer = 0; }
            GC.KeepAlive(devices);
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    internal sealed class VolumeCallback(Action<nint> notify) : IVolumeNotificationCallback
    {
        public int OnNotify(nint data)
        {
            try { notify(data); }
            catch (Exception error) when (error is not OutOfMemoryException) { System.Diagnostics.Debug.WriteLine(error); }
            return 0;
        }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    internal sealed class DeviceCallback(Action changed) : IDefaultAudioDeviceCallback
    {
        private int Notify() { try { changed(); } catch (Exception error) when (error is not OutOfMemoryException) { System.Diagnostics.Debug.WriteLine(error); } return 0; }
        public int OnDeviceStateChanged(string id, uint state) => Notify();
        public int OnDeviceAdded(string id) => Notify();
        public int OnDeviceRemoved(string id) => Notify();
        public int OnDefaultDeviceChanged(int flow, int role, string? id) => flow == (int)EDataFlow.Render && role == (int)ERole.Multimedia ? Notify() : 0;
        public int OnPropertyValueChanged(string id, AudioNotificationPropertyKey key) => 0;
    }
}

// CCW interfaces must be public for Marshal.GetComInterfaceForObject; no class interface is exposed.
[ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IVolumeNotificationCallback { [PreserveSig] int OnNotify(nint data); }

[StructLayout(LayoutKind.Sequential)]
public struct AudioNotificationPropertyKey { public Guid Format; public uint Id; }

[ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDefaultAudioDeviceCallback
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, AudioNotificationPropertyKey key);
}
