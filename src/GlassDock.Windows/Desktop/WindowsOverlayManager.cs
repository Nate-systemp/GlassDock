using System.ComponentModel;
using System.Runtime.InteropServices;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public sealed class WindowsOverlayManager : IDisposable
{
    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;
    public WindowsOverlayManager(nint hwnd) { this.hwnd = hwnd; callback = WindowMessage; }
    public const double Width = 640;
    public const double Height = 144;
    public double Scale => NativeMethods.GetDpiForWindow(hwnd) is var dpi && dpi > 0 ? dpi / 96d : 1;

    public void Configure(bool inspection = false)
    {
        // Only GlassDock's own HWND: tool window, no activation, no caption/resizing frame.
        var ex = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        var extendedStyle = inspection ? ((ex | 0x08040000L) & ~0x80L) : ((ex | 0x08000080L) & ~0x00040000L);
        NativeMethods.SetWindowLongPtr(hwnd, -20, (nint)extendedStyle);
        var style = NativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, -16, (nint)(style & ~0x00CF0000L));
        var noCorner = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref noCorner, sizeof(int));
        ConfigureTransparency();
        if (!NativeMethods.SetWindowSubclass(hwnd, callback, 2, 0))
            throw new InvalidOperationException("Cannot attach overlay transparency handling.");
        var dc = NativeMethods.GetDC(hwnd);
        try { ClearBackground(dc); } finally { NativeMethods.ReleaseDC(hwnd, dc); }
    }

    private void ConfigureTransparency()
    {
        var margins = new NativeMethods.Margins();
        Marshal.ThrowExceptionForHR(NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins));
        var emptyRegion = NativeMethods.CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new NativeMethods.BlurBehind { Flags = 3, Enable = 1, Region = emptyRegion };
            Marshal.ThrowExceptionForHR(NativeMethods.DwmEnableBlurBehindWindow(hwnd, ref blur));
        }
        finally { NativeMethods.DeleteObject(emptyRegion); }
    }

    private bool ClearBackground(nint dc)
    {
        if (dc == 0 || !NativeMethods.GetClientRect(hwnd, out var rect)) return false;
        return NativeMethods.FillRect(dc, ref rect, NativeMethods.GetStockObject(4)) != 0;
    }

    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0014 && ClearBackground((nint)wParam)) return 1;
        if (message == 0x031E) ConfigureTransparency();
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose() => NativeMethods.RemoveWindowSubclass(hwnd, callback, 2);

    public PixelRect Position(double margin)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
        var screen = new PixelRect(info.Monitor.Left, info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
        var rect = DesktopPlacement.BottomCenter(screen, Width, Height, margin, Scale);
        if (!NativeMethods.SetWindowPos(hwnd, -1, rect.X, rect.Y, rect.Width, rect.Height, 0x0010 | 0x0020 | 0x0040))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return rect;
    }

    public void SetInteractionRegion(bool expanded)
    {
        var scale = Scale;
        // The idle hit target surrounds the pill and reaches through its lower margin.
        var x = expanded ? 0 : 220;
        var y = expanded ? 0 : 100;
        var width = expanded ? Width : 200;
        var height = expanded ? Height : 44;
        var region = NativeMethods.CreateRoundRectRgn((int)(x * scale), (int)(y * scale),
            (int)((x + width) * scale), (int)((y + height) * scale), (int)(16 * scale), (int)(16 * scale));
        if (region == 0) throw new Win32Exception();
        if (NativeMethods.SetWindowRgn(hwnd, region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        // Windows owns the region after a successful SetWindowRgn.
    }
}
