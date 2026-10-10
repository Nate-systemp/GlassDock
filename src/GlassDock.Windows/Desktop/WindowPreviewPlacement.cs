using System.Runtime.InteropServices;
using System.ComponentModel;

using GlassDock.Core.Applications;

using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public sealed class WindowPreviewPlacement : IDisposable
{
    private readonly nint window;
    private readonly NativeMethods.SubclassProc callback;
    // Native moves trigger layout and DWM thumbnail updates. A timer frame can
    // land on the same integer-pixel rectangle as its predecessor, especially
    // with DPI scaling; avoid a redundant Win32 resize in that case.
    private PreviewRect? lastPhysicalBounds;

    public WindowPreviewPlacement(nint window, bool inspection = false)
    {
        this.window = window;

        // Configure extended styles.
        var exStyle = NativeMethods.GetWindowLongPtr(window, -20).ToInt64();

        NativeMethods.SetWindowLongPtr(
            window,
            -20,
            (nint)(inspection
                ? (exStyle | 0x080C0000L) & ~0x80L
                : (exStyle | 0x08080080L) & ~0x40000L)
        );
        // Match the dock/utilities' native desktop sampling client.
        if (!NativeMethods.SetLayeredWindowAttributes(window, 0, 255, 2))
            throw new Win32Exception();

        // Remove the native caption / resize / window frame.
        // The preview's glass backdrop draws its own rounded shape.
        var style = NativeMethods.GetWindowLongPtr(window, -16).ToInt64();

        NativeMethods.SetWindowLongPtr(
            window,
            -16,
            (nint)(style & ~0x00CF0000L)
        );

        // Do not let DWM apply its own rounded window frame.
        var noCorner = 1;

        NativeMethods.DwmSetWindowAttribute(
            window,
            33,
            ref noCorner,
            sizeof(int)
        );

        // Disable the Windows 11 native border color.
        var noBorder = unchecked((int)0xFFFFFFFE);

        NativeMethods.DwmSetWindowAttribute(
            window,
            34,
            ref noBorder,
            sizeof(int)
        );

        // Tell Windows the frame styles changed.
        NativeMethods.SetWindowPos(
            window,
            0,
            0,
            0,
            0,
            0,
            0x0001 | // SWP_NOSIZE
            0x0002 | // SWP_NOMOVE
            0x0004 | // SWP_NOZORDER
            0x0010 | // SWP_NOACTIVATE
            0x0020   // SWP_FRAMECHANGED
        );

        var margins = new NativeMethods.Margins();

        NativeMethods.DwmExtendFrameIntoClientArea(
            window,
            ref margins
        );

        var region = NativeMethods.CreateRectRgn(
            -2,
            -2,
            -1,
            -1
        );

        try
        {
            var blur = new NativeMethods.BlurBehind
            {
                Flags = 3,
                Enable = 1,
                Region = region
            };

            NativeMethods.DwmEnableBlurBehindWindow(
                window,
                ref blur
            );
        }
        finally
        {
            NativeMethods.DeleteObject(region);
        }

        callback = (hwnd, message, w, l, id, data) =>
        {
            if (message == 0x21)
                return 3;

            // Keep the compositor host background black/transparent.
            // GetStockObject(4) = BLACK_BRUSH.
            if (message == 0x14 &&
                NativeMethods.GetClientRect(hwnd, out var rect))
            {
                NativeMethods.FillRect(
                    (nint)w,
                    ref rect,
                    NativeMethods.GetStockObject(4)
                );

                return 1;
            }

            return NativeMethods.DefSubclassProc(
                hwnd,
                message,
                w,
                l
            );
        };

        NativeMethods.SetWindowSubclass(
            window,
            callback,
            8,
            0
        );
    }

    public static (
        PreviewRect WorkArea,
        double Dpi,
        PreviewRect Dock
    ) GetArea(nint dock)
    {
        GetWindowRect(
            dock,
            out var rect
        );

        var monitor = NativeMethods.MonitorFromPoint(
            new()
            {
                X = (rect.Left + rect.Right) / 2,
                Y = (rect.Top + rect.Bottom) / 2
            },
            2
        );

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        NativeMethods.GetMonitorInfo(
            monitor,
            ref info
        );

        var scale = GetMonitorScale(monitor, dock);

        return (
            new(
                info.Work.Left,
                info.Work.Top,
                info.Work.Right - info.Work.Left,
                info.Work.Bottom - info.Work.Top
            ),
            scale > 0 ? scale : 1,
            new(
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top
            )
        );
    }

    private static double GetMonitorScale(nint monitor, nint fallbackWindow)
    {
        try
        {
            if (monitor != 0 &&
                NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) >= 0 &&
                dpiX > 0)
            {
                return dpiX / 96d;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        var dpi = NativeMethods.GetDpiForWindow(fallbackWindow);
        return dpi > 0 ? dpi / 96d : 1;
    }

    public void Position(
        nint dock,
        double anchorXDip,
        double dockTopDip,
        double width,
        double height
    )
    {
        var (area, dpi, bounds) = GetArea(dock);

        var rectangle = WindowPreviewLayout.Position(area, bounds, dpi, anchorXDip, dockTopDip, width, height);
        if (!WindowPreviewLayout.NeedsNativeMove(lastPhysicalBounds, rectangle)) return;
        // Cache the physical rectangle only after SetWindowPos succeeds. Otherwise
        // the next frame must retry it instead of leaving a stale placement.
        if (NativeMethods.SetWindowPos(window, -1, (int)rectangle.X, (int)rectangle.Y,
            (int)rectangle.Width, (int)rectangle.Height, 0x10 | 0x40))
            lastPhysicalBounds = rectangle;
    }

    public void Hide()
    {
        // SW_HIDE must be paired with another SWP_SHOWWINDOW even when the
        // next preview opens at exactly the same physical bounds.
        lastPhysicalBounds = null;
        NativeMethods.ShowWindow(
            window,
            0
        );
    }

    public bool ContainsPointer(PreviewRect? clientRegion = null)
    {
        if (!GetCursorPos(out var point) || !GetWindowRect(window, out var bounds) ||
            point.X < bounds.Left || point.X >= bounds.Right ||
            point.Y < bounds.Top || point.Y >= bounds.Bottom) return false;
        if (clientRegion is not { } region) return true;
        var dpi = NativeMethods.GetDpiForWindow(window) / 96d;
        if (dpi <= 0) dpi = 1;
        var x = (point.X - bounds.Left) / dpi;
        var y = (point.Y - bounds.Top) / dpi;
        return x >= region.X && x < region.X + region.Width &&
            y >= region.Y && y < region.Y + region.Height;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativeMethods.Point point);

    public void Dispose()
    {
        NativeMethods.RemoveWindowSubclass(
            window,
            callback,
            8
        );
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(
        nint window,
        out NativeMethods.Rect rect
    );
}
