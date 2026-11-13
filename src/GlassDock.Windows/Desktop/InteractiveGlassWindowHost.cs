using System.ComponentModel;
using System.Runtime.InteropServices;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public sealed class InteractiveGlassWindowHost : IDisposable
{
    private const nuint SubclassId = 3;

    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;

    private bool configured;
    public int? InputHeightPixels { get; set; }
    public bool EnableHostBackdropBrush { get; set; }
    public bool UseDockLayeredTransparency { get; set; }
    public bool PreserveWindowChrome { get; set; }
    public int MinimumWidthDips { get; set; }
    public int MinimumHeightDips { get; set; }
    public bool HostBackdropAvailable { get; private set; }
    public bool ActivateForUserInput() => !disposed && SetForegroundWindow(hwnd);
    public bool HasForegroundOrOwner(nint owner) => !disposed && (GetForegroundWindow() == hwnd || GetForegroundWindow() == owner);
    public bool IsScreenshotForeground() => !disposed && IsScreenshotWindow(GetForegroundWindow());
    internal static bool IsScreenshotWindow(nint window)
    {
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("SnippingTool", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("ScreenClippingHost", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("ScreenSketch", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }
    public bool SetCaptureExclusion(bool excluded) => !disposed && SetWindowDisplayAffinity(hwnd, excluded ? 0x11u : 0u);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
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

        var exStyle =
            NativeMethods
                .GetWindowLongPtr(hwnd, -20)
                .ToInt64();

        if (!PreserveWindowChrome)
        {
            exStyle |= 0x00000080L;
            exStyle &= ~0x00040000L;
        }
        exStyle &= ~0x08000000L;
        if (UseDockLayeredTransparency)
            exStyle |= 0x00080000L;

        NativeMethods.SetWindowLongPtr(hwnd, -20, (nint)exStyle);
        if (UseDockLayeredTransparency &&
            !NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, 2))
        {
            throw new Win32Exception(
                "Failed to configure the Doky layered desktop client.");
        }
        System.Diagnostics.Debug.WriteLine(UseDockLayeredTransparency
            ? "[PopupBackdrop] Dock-matched layered desktop client enabled"
            : "[PopupBackdrop] Existing non-layered window client preserved");

        var style =
            NativeMethods
                .GetWindowLongPtr(hwnd, -16)
                .ToInt64();

        if (!PreserveWindowChrome) NativeMethods.SetWindowLongPtr(
            hwnd,
            -16,
            (nint)(style & ~0x00CF0000L));

        var noCorner = 1;

        if (!PreserveWindowChrome) NativeMethods.DwmSetWindowAttribute(
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

        NativeMethods.SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            0x0001 |
            0x0002 |
            0x0004 |
            0x0010 |
            0x0020);
    }

    private void ConfigureTransparency()
    {
        if (EnableHostBackdropBrush && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            const uint DWMWA_USE_HOSTBACKDROPBRUSH = 17;
            var enabled = 1;
            var result = NativeMethods.DwmSetWindowAttribute(
                hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref enabled, sizeof(int));
            HostBackdropAvailable = result >= 0;
            System.Diagnostics.Debug.WriteLine(HostBackdropAvailable
                ? "[PopupBackdrop] DWM host backdrop sampling enabled"
                : $"[PopupBackdrop] DWM host backdrop unavailable (0x{result:X8})");
        }
        var noBorder = unchecked((int)0xFFFFFFFE);
        NativeMethods.DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int));
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
        if (message == 0x0024 && (MinimumWidthDips > 0 || MinimumHeightDips > 0))
        {
            var result = NativeMethods.DefSubclassProc(window, message, wParam, lParam);
            var limits = Marshal.PtrToStructure<WindowSizeLimits>(lParam);
            var dpiScale = NativeMethods.GetDpiForWindow(hwnd) / 96d;
            limits.MinimumX = (int)Math.Round(MinimumWidthDips * dpiScale);
            limits.MinimumY = (int)Math.Round(MinimumHeightDips * dpiScale);
            Marshal.StructureToPtr(limits, lParam, false);
            return result;
        }
        if (message == 0x0084 && InputHeightPixels is { } height &&
            NativeMethods.GetWindowRect(hwnd, out var bounds))
        {
            var screenY = (short)((lParam.ToInt64() >> 16) & 0xffff);
            if (screenY - bounds.Top >= height) return -1;
        }
        if (message == 0x0014 &&
            ClearBackground((nint)wParam))
        {
            return 1;
        }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSizeLimits
    {
        public int ReservedX, ReservedY, MaximumX, MaximumY, PositionX, PositionY;
        public int MinimumX, MinimumY, MaximumTrackX, MaximumTrackY;
    }
}
