using System.ComponentModel;
using System.Runtime.InteropServices;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

/// <summary>
/// Gives a normal interactive WinUI window the same native transparent
/// DWM client treatment used by GlassDock's desktop overlay, without
/// turning the window into WS_EX_NOACTIVATE.
/// </summary>
public sealed class InteractiveGlassWindowHost : IDisposable
{
    private const nuint SubclassId = 3;

    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;

    private bool configured;
    // Optional transparent travel space below a utility popup. The dock belongs
    // to the same UI thread, so HTTRANSPARENT forwards clicks to its controls.
    public int? InputHeightPixels { get; set; }
    // Set by utility windows before Configure(). Glass Home retains its
    // existing native transparency behavior without a host brush opt-in.
    public bool EnableHostBackdropBrush { get; set; }
    // Unlike Glass Home/preview windows, the three dock utilities need the
    // SAME layered DWM client that the main dock uses for its desktop backdrop.
    // Configure before the XAML SystemBackdrop makes its first connection.
    public bool UseDockLayeredTransparency { get; set; }
    public bool HostBackdropAvailable { get; private set; }
    private bool disposed;

    public InteractiveGlassWindowHost(nint hwnd)
    {
        this.hwnd = hwnd;

        callback = WindowMessage;
    }

    public void Configure()
    {
        if (disposed)
            throw new ObjectDisposedException(
                nameof(InteractiveGlassWindowHost));

        if (configured)
            return;

        //
        // Keep this an interactive tool window:
        // - hide it from Alt+Tab / normal app-window treatment
        // - DO NOT use WS_EX_NOACTIVATE
        //
        var exStyle =
            NativeMethods
                .GetWindowLongPtr(hwnd, -20)
                .ToInt64();

        exStyle |= 0x00000080L;   // WS_EX_TOOLWINDOW
        exStyle &= ~0x00040000L;  // WS_EX_APPWINDOW
        exStyle &= ~0x08000000L;  // WS_EX_NOACTIVATE (must stay OFF)
        if (UseDockLayeredTransparency)
            exStyle |= 0x00080000L; // WS_EX_LAYERED, as on the main dock

        NativeMethods.SetWindowLongPtr(hwnd, -20, (nint)exStyle);
        if (UseDockLayeredTransparency &&
            !NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, 2))
        {
            throw new Win32Exception(
                "Failed to configure the Doky utility layered desktop client.");
        }
        System.Diagnostics.Debug.WriteLine(UseDockLayeredTransparency
            ? "[UtilityBackdrop] Dock-matched layered desktop client enabled"
            : "[UtilityBackdrop] Existing non-layered window client preserved");

        //
        // Remove the ordinary native frame.
        //
        var style =
            NativeMethods
                .GetWindowLongPtr(hwnd, -16)
                .ToInt64();

        NativeMethods.SetWindowLongPtr(
            hwnd,
            -16,
            (nint)(style & ~0x00CF0000L));

        //
        // WinUI / GlassSurface owns the rounded shape.
        //
        var noCorner = 1;

        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            33,
            ref noCorner,
            sizeof(int));

        ConfigureTransparency();

        if (!NativeMethods.SetWindowSubclass(
                hwnd,
                callback,
                SubclassId,
                0))
        {
            throw new InvalidOperationException(
                "Cannot attach Glass Home transparency handling.");
        }

        configured = true;

        //
        // Same important native client clear used by the dock.
        // Under the DWM glass frame, black client pixels become the
        // transparent composition base instead of an opaque WinUI surface.
        //
        var dc =
            NativeMethods.GetDC(hwnd);

        try
        {
            ClearBackground(dc);
        }
        finally
        {
            NativeMethods.ReleaseDC(
                hwnd,
                dc);
        }

        //
        // Make the non-client/client style update take effect without
        // moving, resizing, or activating the window.
        //
        NativeMethods.SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            0x0001 | // SWP_NOSIZE
            0x0002 | // SWP_NOMOVE
            0x0004 | // SWP_NOZORDER
            0x0010 | // SWP_NOACTIVATE
            0x0020); // SWP_FRAMECHANGED
    }

    private void ConfigureTransparency()
    {
        // Required by DWM for CreateHostBackdropBrush on a desktop HWND.
        // Set before WinUI's SystemBackdrop connects and repeat after DWM
        // composition resets. Ignored on Windows versions lacking this flag.
        if (EnableHostBackdropBrush && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            const uint DWMWA_USE_HOSTBACKDROPBRUSH = 17;
            var enabled = 1;
            var result = NativeMethods.DwmSetWindowAttribute(
                hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref enabled, sizeof(int));
            HostBackdropAvailable = result >= 0;
            System.Diagnostics.Debug.WriteLine(HostBackdropAvailable
                ? "[UtilityBackdrop] DWM host backdrop sampling enabled"
                : $"[UtilityBackdrop] DWM host backdrop unavailable (0x{result:X8})");
        }
        // Win11's DWM outline surrounds the rectangular HWND, outside our rounded glass mask.
        // Suppress only this window's outline; unsupported Windows versions simply ignore it.
        var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        NativeMethods.DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int)); // DWMWA_BORDER_COLOR
        //
        // Mirror the native transparency path used by WindowsOverlayManager.
        //
        var margins =
            new NativeMethods.Margins();

        Marshal.ThrowExceptionForHR(
            NativeMethods.DwmExtendFrameIntoClientArea(
                hwnd,
                ref margins));

        var emptyRegion =
            NativeMethods.CreateRectRgn(
                -2,
                -2,
                -1,
                -1);

        if (emptyRegion == 0)
            throw new InvalidOperationException(
                "Cannot create Glass Home DWM region.");

        try
        {
            var blur =
                new NativeMethods.BlurBehind
                {
                    Flags = 3,
                    Enable = 1,
                    Region = emptyRegion
                };

            Marshal.ThrowExceptionForHR(
                NativeMethods.DwmEnableBlurBehindWindow(
                    hwnd,
                    ref blur));
        }
        finally
        {
            NativeMethods.DeleteObject(
                emptyRegion);
        }
    }

    private bool ClearBackground(nint dc)
    {
        if (dc == 0 ||
            !NativeMethods.GetClientRect(
                hwnd,
                out var rect))
        {
            return false;
        }

        return NativeMethods.FillRect(
                   dc,
                   ref rect,
                   NativeMethods.GetStockObject(4))
               != 0;
    }

    private nint WindowMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint id,
        nuint data)
    {
        if (message == 0x0084 && InputHeightPixels is { } height &&
            NativeMethods.GetWindowRect(hwnd, out var bounds))
        {
            var screenY = (short)((lParam.ToInt64() >> 16) & 0xffff);
            if (screenY - bounds.Top >= height) return -1; // HTTRANSPARENT
        }
        //
        // WM_ERASEBKGND:
        // prevent the default opaque WinUI/native client background
        // from becoming the visible base behind the system backdrop.
        //
        if (message == 0x0014 &&
            ClearBackground((nint)wParam))
        {
            return 1;
        }

        //
        // WM_DWMCOMPOSITIONCHANGED:
        // reapply native glass if DWM recreates composition state.
        //
        if (message == 0x031E)
        {
            ConfigureTransparency();
        }

        return NativeMethods.DefSubclassProc(
            window,
            message,
            wParam,
            lParam);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (configured)
        {
            NativeMethods.RemoveWindowSubclass(
                hwnd,
                callback,
                SubclassId);

            configured = false;
        }

        GC.KeepAlive(callback);
    }
}
