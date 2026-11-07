using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

internal sealed record AirplaneModeResult(bool? On, Exception? Error = null);

// Private ABI, verified against RadioMan and SleepToAirPlane; never enumerate/set individual radios.
internal static class AirplaneModeNative
{
    private static readonly Guid ClassId = new("581333f6-28db-41be-bc7a-ff201f12f3f6");
    private static readonly Guid InterfaceId = new("db3afbfb-08e6-46c6-aa70-bf9a34c30ab7");

    internal static int RadioEnabledForAirplane(bool on) => on ? 0 : 1;
    internal static bool AirplaneFromRadioEnabled(int enabled) => enabled switch
    {
        0 => true,
        1 => false,
        _ => throw new NotSupportedException("Windows returned an unknown global radio state.")
    };

    internal static AirplaneModeResult Run(bool toggle, CancellationToken cancel)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2)); // Dedicated STA; release on this same thread.
        IRadioManager? manager = null;
        bool? actual = null;
        try
        {
            cancel.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(CoCreateInstance(in ClassId, 0, 4, in InterfaceId, out manager));
            Marshal.ThrowExceptionForHR(manager.IsRMSupported(out var supported));
            if (supported == 0) throw new NotSupportedException("Global radio management is unavailable on this device.");
            actual = Read(manager);
            if (toggle)
            {
                var desired = !actual.Value;
                cancel.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(manager.SetSystemRadioState(RadioEnabledForAirplane(desired)));
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    actual = Read(manager);
                    if (actual == desired) break;
                    // Observe asynchronous Windows state changes, bounded by the operation token.
                    if (cancel.WaitHandle.WaitOne(100)) cancel.ThrowIfCancellationRequested();
                }
            }
            return new(actual);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A failed setter can still have changed Windows. Recover from the getter, never the request.
            actual = null;
            if (manager is not null && !cancel.IsCancellationRequested)
            {
                try { actual = Read(manager); }
                catch (Exception recovery) when (recovery is not OutOfMemoryException)
                { System.Diagnostics.Debug.WriteLine($"Airplane mode recovery: {recovery.Message}"); }
            }
            return new(actual, error);
        }
        finally
        {
            try { if (manager is not null) Marshal.FinalReleaseComObject(manager); }
            finally { CoUninitialize(); }
        }
    }

    private static bool Read(IRadioManager manager)
    {
        Marshal.ThrowExceptionForHR(manager.GetSystemRadioState(out var enabled, out _, out _));
        return AirplaneFromRadioEnabled(enabled);
    }

    [ComImport, Guid("db3afbfb-08e6-46c6-aa70-bf9a34c30ab7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRadioManager
    {
        [PreserveSig] int IsRMSupported(out uint supported);
        [PreserveSig] int GetUIRadioInstances(out nint instances);
        [PreserveSig] int GetSystemRadioState(out int enabled, out int flags, out int reason);
        [PreserveSig] int SetSystemRadioState(int enabled);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IRadioManager manager);
}
