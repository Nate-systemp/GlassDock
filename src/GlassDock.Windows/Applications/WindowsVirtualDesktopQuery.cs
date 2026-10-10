using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GlassDock.Windows.Applications;

// Owned by one enumeration on its calling apartment. Recreating on the next
// reconciliation also recovers from Explorer/COM server replacement.
internal sealed class WindowsVirtualDesktopQuery : IDisposable
{
    private IVirtualDesktopManager? manager;

    public WindowsVirtualDesktopQuery()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
            if (type is not null) manager = (IVirtualDesktopManager?)Activator.CreateInstance(type);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or TypeLoadException) { }
    }

    public bool? IsCurrent(nint window)
    {
        // Some Shell versions report current=true for an invalid HWND. A window
        // may disappear between enumeration and the COM call.
        if (!ApplicationNative.IsWindow(window)) return null;
        return QuerySafely(() =>
        {
            if (manager is null) return null;
            var hr = manager.IsWindowOnCurrentVirtualDesktop(window, out var current);
            return hr >= 0 && ApplicationNative.IsWindow(window) ? current : null;
        });
    }

    internal static bool? QuerySafely(Func<bool?> query)
    {
        try { return query(); }
        catch (Exception error) when (error is COMException or InvalidComObjectException)
        {
            System.Diagnostics.Debug.WriteLine($"Virtual desktop query unavailable: 0x{error.HResult:X8}");
            return null;
        }
    }

    public static bool ShowAllWindows()
    {
        // Explorer preference, read-only and best effort; not a documented API.
        // Missing/unrecognized values safely default to current desktop only.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return key?.GetValue("VirtualDesktopTaskbarFilter") is int value && value == 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }

    public void Dispose()
    {
        if (manager is not null) Marshal.ReleaseComObject(manager);
        manager = null;
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktopId);
    }
}
