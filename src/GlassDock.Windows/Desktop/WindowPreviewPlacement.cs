using System.Runtime.InteropServices;

using GlassDock.Core.Applications;

using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public sealed class WindowPreviewPlacement : IDisposable
{
    private readonly nint window;
    private readonly NativeMethods.SubclassProc callback;

    public WindowPreviewPlacement(nint window, bool inspection = false)
    {
        this.window = window;

        // Configure extended styles.
        var exStyle = NativeMethods.GetWindowLongPtr(window, -20).ToInt64();

        NativeMethods.SetWindowLongPtr(
            window,
            -20,
            (nint)(inspection
                ? (exStyle | 0x08040000L) & ~0x80L
                : (exStyle | 0x08000080L) & ~0x40000L)
        );

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

        var scale =
            NativeMethods.GetDpiForWindow(dock) / 96d;

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

    public void Position(
        nint dock,
        double anchorXDip,
        double dockTopDip,
        double width,
        double height
    )
    {
        var (area, dpi, bounds) = GetArea(dock);

        var pixelWidth =
            (int)Math.Ceiling(width * dpi);

        var pixelHeight =
            (int)Math.Ceiling(height * dpi);

        var x = (int)Math.Clamp(
            bounds.X +
            anchorXDip * dpi -
            pixelWidth / 2d,
            area.X,
            Math.Max(
                area.X,
                area.X +
                area.Width -
                pixelWidth
            )
        );

        var y = (int)Math.Clamp(
            bounds.Y +
            dockTopDip * dpi -
            pixelHeight,
            area.Y,
            Math.Max(
                area.Y,
                area.Y +
                area.Height -
                pixelHeight
            )
        );

        NativeMethods.SetWindowPos(
            window,
            -1,
            x,
            y,
            pixelWidth,
            pixelHeight,
            0x10 | 0x40
        );
    }

    public void Hide()
    {
        NativeMethods.ShowWindow(
            window,
            0
        );
    }

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