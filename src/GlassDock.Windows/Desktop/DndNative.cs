using System.Runtime.InteropServices;
using ApiInformation = Windows.Foundation.Metadata.ApiInformation;
using Windows.UI.Notifications;

namespace GlassDock.Windows.Desktop;

internal sealed class DndNative : IDisposable
{
    internal const string OffProfile = "Microsoft.QuietHoursProfile.Unrestricted";
    internal const string OnProfile = "Microsoft.QuietHoursProfile.PriorityOnly";
    internal const string AlarmsProfile = "Microsoft.QuietHoursProfile.AlarmsOnly";
    private readonly ToastNotificationManagerForUser manager;
    private readonly Action<uint?> changed;
    private readonly object sync = new();
    private TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long version;
    private bool disposed;
    internal bool HasNotifications { get; }

    internal DndNative(Action<uint?> changed)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100) ||
            !ApiInformation.IsPropertyPresent("Windows.UI.Notifications.ToastNotificationManagerForUser", "NotificationMode"))
            throw new NotSupportedException("Windows does not expose actual Do Not Disturb notification mode.");
        this.changed = changed;
        manager = ToastNotificationManager.GetDefault();
        HasNotifications = ApiInformation.IsEventPresent("Windows.UI.Notifications.ToastNotificationManagerForUser", "NotificationModeChanged");
        if (HasNotifications) manager.NotificationModeChanged += ModeChanged;
    }

    internal uint ReadMode()
    {
        var actual = (uint)manager.NotificationMode;
        _ = IsOn(actual);
        return actual;
    }

    internal static bool IsOn(uint mode) => mode switch
    {
        0 => false,
        1 or 2 => true,
        _ => throw new NotSupportedException("Windows returned an unknown notification mode.")
    };

    internal static string ProfileFor(bool on) => on ? OnProfile : OffProfile;
    internal static void ValidateProfile(string? profile)
    {
        if (profile is not (OffProfile or OnProfile or AlarmsProfile))
            throw new NotSupportedException("Windows returned an unknown QuietHours profile; no change was made.");
    }

    internal async Task<uint> ConfirmAsync(uint target, CancellationToken cancellation)
    {
        while (true)
        {
            Task wait;
            long observed;
            lock (sync) { if (disposed) throw new ObjectDisposedException(nameof(DndNative)); observed = version; wait = signal.Task; }
            var actual = ReadMode();
            if (actual == target) return actual;
            if (!HasNotifications) { await Task.Delay(100, cancellation).ConfigureAwait(false); continue; }
            lock (sync) { if (version != observed) continue; }
            await wait.WaitAsync(cancellation).ConfigureAwait(false);
        }
    }

    private void ModeChanged(ToastNotificationManagerForUser sender, object args)
    {
        uint? actual = null;
        try { actual = ReadMode(); }
        catch (Exception error) when (error is not OutOfMemoryException) { System.Diagnostics.Debug.WriteLine(error); }
        lock (sync)
        {
            if (disposed) return;
            version++; signal.TrySetResult(); signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { changed(actual); }
        catch (Exception error) when (error is not OutOfMemoryException) { System.Diagnostics.Debug.WriteLine(error); }
    }

    internal static void SetProfile(bool on, CancellationToken cancellation)
    {
        // ABI and profile strings verified against YASB's QuietHoursSettings bindings.
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
        IQuietHoursSettings? settings = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var clsid = new Guid("F53321FA-34F8-4B7F-B9A3-361877CB94CF");
            var iid = typeof(IQuietHoursSettings).GUID;
            Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 4, in iid, out settings));
            nint profile = 0;
            try
            {
                Marshal.ThrowExceptionForHR(settings.GetUserSelectedProfile(out profile));
                ValidateProfile(Marshal.PtrToStringUni(profile));
            }
            finally { if (profile != 0) Marshal.FreeCoTaskMem(profile); }
            cancellation.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(settings.PutUserSelectedProfile(ProfileFor(on)));
        }
        finally
        {
            try { if (settings is not null) Marshal.FinalReleaseComObject(settings); }
            finally { CoUninitialize(); }
        }
    }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; signal.TrySetResult(); }
        if (HasNotifications)
        {
            try { manager.NotificationModeChanged -= ModeChanged; }
            catch (Exception error) when (error is COMException or InvalidOperationException)
            { System.Diagnostics.Debug.WriteLine(error); }
        }
    }

    [ComImport, Guid("6BFF4732-81EC-4FFB-AE67-B6C1BC29631F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IQuietHoursSettings
    {
        [PreserveSig] int GetUserSelectedProfile(out nint profile);
        [PreserveSig] int PutUserSelectedProfile([MarshalAs(UnmanagedType.LPWStr)] string profile);
    }
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IQuietHoursSettings settings);
}
